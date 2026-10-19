# /// script
# requires-python = ">=3.10"
# dependencies = [
#   "tree-sitter>=0.23,<1",
#   "tree-sitter-c-sharp>=0.23,<1",
# ]
# ///
"""從指定入口類別向下解析 C# 專案，輸出上限內專案類別的 JSON metadata。

用法：
    uv run scripts/csharp_class_graph.py --root <專案根目錄> --entry <入口類別> [--max-classes 30]

輸出（stdout 或 --output 指定檔案）為 JSON：
    classes            上限內的專案類別（含成員簽章、檔案位置、BFS 深度）
    relations          上限內類別彼此之間的關係（inherits / implements / association / dependency）
    externalReferences BCL 或外部套件型別，僅記錄引用次數與引用者，不佔類別名額
    truncated          因名額已滿而未納入、但與已納入類別直接相連的專案類別
    warnings           解析限制或參數調整提示

Exit codes：
    0 成功；2 參數錯誤或找不到 .cs 檔；3 找不到入口類別；4 入口類別名稱歧義；5 缺少 parser 依賴
"""

from __future__ import annotations

import argparse
import difflib
import json
import sys
from collections import deque
from dataclasses import dataclass, field
from pathlib import Path

HARD_MAX_CLASSES = 30
SKIP_DIRS = {"bin", "obj", ".git", ".vs", "node_modules"}

TYPE_DECL_KINDS = {
    "class_declaration": "class",
    "interface_declaration": "interface",
    "struct_declaration": "struct",
    "record_declaration": "record",
    "record_struct_declaration": "record struct",
    "enum_declaration": "enum",
    "delegate_declaration": "delegate",
}

# C# 關鍵字型別別名：完全忽略，不列入 externalReferences
BUILTIN_TYPES = {
    "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint",
    "nint", "nuint", "long", "ulong", "short", "ushort", "object", "string", "void",
    "dynamic", "var",
}

# 型別引數視為「多個」的集合型別
COLLECTION_TYPES = {
    "List", "IList", "IReadOnlyList", "ICollection", "IReadOnlyCollection", "IEnumerable",
    "HashSet", "ISet", "IReadOnlySet", "SortedSet", "Queue", "Stack", "LinkedList",
    "Dictionary", "IDictionary", "IReadOnlyDictionary", "SortedDictionary",
    "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentBag", "ImmutableList",
    "ImmutableArray", "IAsyncEnumerable", "Collection", "ObservableCollection",
}

VISIBLE_MODIFIERS = {"public", "internal", "protected"}
ACCESS_MODIFIERS = {"public", "internal", "protected", "private"}

RELATION_PRIORITY = {"inherits": 1, "implements": 1, "association": 2, "dependency": 3}


# ---------------------------------------------------------------------------
# 資料結構
# ---------------------------------------------------------------------------


@dataclass
class TypeRef:
    """原始碼中的一次型別引用。"""

    name: str  # 簡單名稱，例如 Route
    qualified: str  # 原始書寫名稱（含 qualifier），例如 System.IO.Stream
    many: bool = False


@dataclass
class RawEdge:
    refs: list[TypeRef]
    rel: str  # base / association / dependency
    via: str | None = None


@dataclass
class TypeDecl:
    full_name: str
    name: str
    kind: str
    namespace: str
    modifiers: list[str]
    file: str
    line: int
    usings: list[str]
    type_params: set[str]
    members: list[dict] = field(default_factory=list)
    edges: list[RawEdge] = field(default_factory=list)
    partial_files: list[str] = field(default_factory=list)
    extra: dict = field(default_factory=dict)


# ---------------------------------------------------------------------------
# 語法樹工具
# ---------------------------------------------------------------------------


def text(node) -> str:
    return node.text.decode("utf-8", errors="replace") if node is not None else ""


def modifiers_of(node) -> list[str]:
    return [text(c) for c in node.children if c.type == "modifier"]


def visibility_of(mods: list[str], default: str) -> str:
    access = [m for m in mods if m in ACCESS_MODIFIERS]
    return " ".join(access) if access else default


def collect_type_refs(node, many: bool = False, out: list[TypeRef] | None = None) -> list[TypeRef]:
    """把型別節點拆成簡單名稱清單；泛型引數、陣列、nullable 皆會展開。"""
    if out is None:
        out = []
    if node is None:
        return out
    t = node.type
    if t in ("predefined_type", "implicit_type"):
        return out
    if t == "identifier":
        out.append(TypeRef(text(node), text(node), many))
    elif t == "generic_name":
        ident = next((c for c in node.children if c.type == "identifier"), None)
        base = text(ident)
        out.append(TypeRef(base, base, many))
        args = next((c for c in node.children if c.type == "type_argument_list"), None)
        if args is not None:
            arg_many = many or base in COLLECTION_TYPES
            for a in args.named_children:
                collect_type_refs(a, arg_many, out)
    elif t == "qualified_name":
        name_node = node.child_by_field_name("name")
        qualifier = text(node.child_by_field_name("qualifier"))
        if name_node is not None and name_node.type == "generic_name":
            start = len(out)
            collect_type_refs(name_node, many, out)
            if len(out) > start:
                first = out[start]
                out[start] = TypeRef(first.name, f"{qualifier}.{first.name}", first.many)
        else:
            out.append(TypeRef(text(name_node), text(node), many))
    elif t == "alias_qualified_name":
        name_node = node.child_by_field_name("name")
        collect_type_refs(name_node, many, out)
    elif t == "array_type":
        collect_type_refs(node.child_by_field_name("type"), True, out)
    elif t in ("nullable_type", "pointer_type", "ref_type", "scoped_type"):
        inner = node.child_by_field_name("type") or (node.named_children[0] if node.named_children else None)
        collect_type_refs(inner, many, out)
    else:
        for c in node.named_children:
            collect_type_refs(c, many, out)
    return out


def walk_skip_nested_types(node):
    """深度走訪節點，但不進入巢狀型別宣告。"""
    stack = list(reversed(node.children))
    while stack:
        n = stack.pop()
        yield n
        if n.type in TYPE_DECL_KINDS:
            continue
        stack.extend(reversed(n.children))


def param_signatures(param_list) -> tuple[list[str], list[TypeRef]]:
    sigs: list[str] = []
    refs: list[TypeRef] = []
    if param_list is None:
        return sigs, refs
    for p in param_list.named_children:
        if p.type != "parameter":
            continue
        ptype = p.child_by_field_name("type")
        pname = text(p.child_by_field_name("name"))
        sigs.append(f"{text(ptype)} {pname}".strip())
        collect_type_refs(ptype, out=refs)
    return sigs, refs


def type_param_names(node) -> set[str]:
    names: set[str] = set()
    for c in node.children:
        if c.type == "type_parameter_list":
            for tp in c.named_children:
                if tp.type == "type_parameter":
                    names.add(text(tp.child_by_field_name("name")))
    return names


def enclosing_method_type_params(node, stop) -> set[str]:
    """回傳 node 所在方法宣告的泛型參數名稱；走到 stop 節點即停止。"""
    cur = node.parent
    while cur is not None and cur != stop:
        if cur.type in ("method_declaration", "local_function_statement"):
            return type_param_names(cur)
        cur = cur.parent
    return set()


def constraint_refs(node) -> list[TypeRef]:
    refs: list[TypeRef] = []
    for c in node.children:
        if c.type == "type_parameter_constraints_clause":
            for tc in c.named_children:
                if tc.type == "type_parameter_constraint":
                    collect_type_refs(tc.child_by_field_name("type"), out=refs)
    return refs


# ---------------------------------------------------------------------------
# 索引建立
# ---------------------------------------------------------------------------


class Indexer:
    def __init__(self, root: Path, parser):
        self.root = root
        self.parser = parser
        self.decls: dict[str, TypeDecl] = {}
        self.warnings: list[str] = []
        self._global_usings: set[str] = set()
        self._alias_seen = False

    def index_file(self, path: Path) -> None:
        src = path.read_bytes()
        tree = self.parser.parse(src)
        rel = path.relative_to(self.root).as_posix()
        if tree.root_node.has_error:
            self.warnings.append(f"語法解析有錯誤節點，結果可能不完整：{rel}")
        self._walk_container(tree.root_node, ns="", usings=[], outer=[], rel=rel)

    def finalize(self) -> None:
        if self._global_usings:
            for d in self.decls.values():
                d.usings = sorted(set(d.usings) | self._global_usings)
            self.warnings.append("偵測到 global using，已套用到所有檔案的命名空間解析。")
        if self._alias_seen:
            self.warnings.append("偵測到 using 別名（using X = ...），別名型別不會被解析為專案類別。")

    def _walk_container(self, node, ns: str, usings: list[str], outer: list[str], rel: str) -> None:
        usings = list(usings)
        current_ns = ns
        for child in node.named_children:
            t = child.type
            if t == "using_directive":
                self._handle_using(child, usings)
            elif t == "namespace_declaration":
                name = text(child.child_by_field_name("name"))
                body = child.child_by_field_name("body")
                new_ns = f"{current_ns}.{name}" if current_ns else name
                if body is not None:
                    self._walk_container(body, new_ns, usings, outer, rel)
            elif t == "file_scoped_namespace_declaration":
                name = text(child.child_by_field_name("name"))
                current_ns = f"{ns}.{name}" if ns else name
                # 部分 grammar 版本會把後續宣告放在此節點內
                self._walk_container(child, current_ns, usings, outer, rel)
            elif t in TYPE_DECL_KINDS:
                self._handle_type(child, current_ns, usings, outer, rel)

    def _handle_using(self, node, usings: list[str]) -> None:
        is_global = any(c.type == "global" for c in node.children)
        has_alias = any(c.type == "=" for c in node.children)
        is_static = any(c.type == "static" for c in node.children)
        if has_alias:
            self._alias_seen = True
            return
        target = next(
            (c for c in node.named_children if c.type in ("identifier", "qualified_name")),
            None,
        )
        if target is None or is_static:
            return
        name = text(target)
        if is_global:
            self._global_usings.add(name)
        usings.append(name)

    def _handle_type(self, node, ns: str, usings: list[str], outer: list[str], rel: str) -> None:
        kind = TYPE_DECL_KINDS[node.type]
        if node.type == "record_declaration" and any(c.type == "struct" for c in node.children):
            kind = "record struct"
        name = text(node.child_by_field_name("name"))
        nested_path = outer + [name]
        prefix = f"{ns}." if ns else ""
        full_name = prefix + ".".join(nested_path)
        mods = modifiers_of(node)

        decl = TypeDecl(
            full_name=full_name,
            name=name,
            kind=kind,
            namespace=ns,
            modifiers=mods,
            file=rel,
            line=node.start_point[0] + 1,
            usings=list(usings),
            type_params=type_param_names(node),
        )
        self._extract(node, decl)

        if full_name in self.decls:  # partial 類別合併
            existing = self.decls[full_name]
            existing.members.extend(decl.members)
            existing.edges.extend(decl.edges)
            existing.type_params |= decl.type_params
            existing.usings = sorted(set(existing.usings) | set(decl.usings))
            if rel != existing.file and rel not in existing.partial_files:
                existing.partial_files.append(rel)
        else:
            self.decls[full_name] = decl

        body = node.child_by_field_name("body")
        if body is not None and body.type == "declaration_list":
            for child in body.named_children:
                if child.type in TYPE_DECL_KINDS:
                    self._handle_type(child, ns, usings, nested_path, rel)

    # -- 宣告內容抽取 -----------------------------------------------------

    def _extract(self, node, decl: TypeDecl) -> None:
        is_interface = decl.kind == "interface"
        member_default = "public" if is_interface else "private"

        # 繼承清單
        for c in node.children:
            if c.type == "base_list":
                for b in c.named_children:
                    target = b
                    if b.type == "primary_constructor_base_type":
                        target = b.child_by_field_name("type") or (b.named_children[0] if b.named_children else b)
                    refs = collect_type_refs(target)
                    if refs:
                        # 第一個為主型別，其餘為泛型引數（視為 dependency）
                        decl.edges.append(RawEdge([refs[0]], "base"))
                        if len(refs) > 1:
                            decl.edges.append(RawEdge(refs[1:], "dependency", "base-type-argument"))

        decl.edges.append(RawEdge(constraint_refs(node), "dependency", "constraint"))

        if decl.kind == "delegate":
            ret = node.child_by_field_name("type")
            sigs, prefs = param_signatures(node.child_by_field_name("parameters"))
            decl.extra["signature"] = {"returns": text(ret), "params": sigs}
            decl.edges.append(RawEdge(collect_type_refs(ret) + prefs, "dependency", "signature"))
            return

        if decl.kind == "enum":
            body = node.child_by_field_name("body")
            if body is not None:
                decl.extra["values"] = [
                    text(m.child_by_field_name("name"))
                    for m in body.named_children
                    if m.type == "enum_member_declaration"
                ]
            return

        # record / class primary constructor 參數
        primary = next((c for c in node.children if c.type == "parameter_list"), None)
        if primary is not None:
            sigs, prefs = param_signatures(primary)
            rel = "association" if decl.kind.startswith("record") else "dependency"
            decl.edges.append(RawEdge(prefs, rel, "primary-constructor"))
            decl.members.append(
                {"kind": "constructor", "name": decl.name, "params": sigs, "visibility": "public", "primary": True}
            )

        body = node.child_by_field_name("body")
        if body is None or body.type != "declaration_list":
            return

        for m in body.named_children:
            t = m.type
            mods = modifiers_of(m)
            vis = visibility_of(mods, member_default)
            is_static = "static" in mods
            if t in ("field_declaration", "event_field_declaration"):
                var = next((c for c in m.named_children if c.type == "variable_declaration"), None)
                if var is None:
                    continue
                vtype = var.child_by_field_name("type")
                refs = collect_type_refs(vtype)
                for d in var.named_children:
                    if d.type != "variable_declarator":
                        continue
                    vname = text(d.child_by_field_name("name"))
                    self._add_member(decl, {
                        "kind": "event" if t == "event_field_declaration" else "field",
                        "name": vname, "type": text(vtype), "visibility": vis,
                    }, is_static)
                    decl.edges.append(RawEdge(refs, "association", vname))
            elif t in ("property_declaration", "event_declaration", "indexer_declaration"):
                ptype = m.child_by_field_name("type")
                pname = text(m.child_by_field_name("name")) or "this[]"
                kind = {"property_declaration": "property", "event_declaration": "event",
                        "indexer_declaration": "indexer"}[t]
                self._add_member(decl, {"kind": kind, "name": pname, "type": text(ptype), "visibility": vis}, is_static)
                decl.edges.append(RawEdge(collect_type_refs(ptype), "association", pname))
            elif t in ("method_declaration", "operator_declaration", "conversion_operator_declaration"):
                ret = m.child_by_field_name("returns") or m.child_by_field_name("type")
                mname = text(m.child_by_field_name("name")) or t.replace("_declaration", "")
                sigs, prefs = param_signatures(m.child_by_field_name("parameters"))
                local_tparams = type_param_names(m)
                self._add_member(decl, {
                    "kind": "method", "name": mname, "returns": text(ret), "params": sigs, "visibility": vis,
                }, is_static)
                refs = collect_type_refs(ret) + prefs + constraint_refs(m)
                refs = [r for r in refs if r.name not in local_tparams]
                decl.edges.append(RawEdge(refs, "dependency", mname))
            elif t == "constructor_declaration":
                sigs, prefs = param_signatures(m.child_by_field_name("parameters"))
                self._add_member(decl, {
                    "kind": "constructor", "name": decl.name, "params": sigs, "visibility": vis,
                }, is_static)
                decl.edges.append(RawEdge(prefs, "dependency", "constructor"))

        # new X() 建立的型別（排除所在方法自身的泛型參數，例如 new TMiddleware()）
        new_refs: list[TypeRef] = []
        for n in walk_skip_nested_types(body):
            if n.type == "object_creation_expression":
                refs = collect_type_refs(n.child_by_field_name("type"))
                local_tparams = enclosing_method_type_params(n, body)
                new_refs.extend(r for r in refs if r.name not in local_tparams)
        decl.edges.append(RawEdge(new_refs, "dependency", "new"))

    @staticmethod
    def _add_member(decl: TypeDecl, member: dict, is_static: bool) -> None:
        if is_static:
            member["static"] = True
        decl.members.append(member)


# ---------------------------------------------------------------------------
# 型別解析與 BFS
# ---------------------------------------------------------------------------


class Resolver:
    def __init__(self, decls: dict[str, TypeDecl], warnings: list[str]):
        self.decls = decls
        self.warnings = warnings
        self.by_name: dict[str, list[TypeDecl]] = {}
        for d in decls.values():
            self.by_name.setdefault(d.name, []).append(d)
        self._warned_ambiguous: set[str] = set()

    def resolve(self, ref: TypeRef, ctx: TypeDecl) -> str | None:
        """回傳專案內型別的 full_name；找不到時回傳 None（視為 external）。"""
        candidates = self.by_name.get(ref.name, [])
        if not candidates:
            return None
        if "." in ref.qualified:
            q = ref.qualified
            matched = [c for c in candidates if c.full_name == q or c.full_name.endswith("." + q)]
            if not matched:
                return None
            candidates = matched
        if len(candidates) == 1:
            return candidates[0].full_name

        # 巢狀型別：優先同一外層
        nested = [c for c in candidates if c.full_name.startswith(ctx.full_name + ".")]
        if len(nested) == 1:
            return nested[0].full_name
        # 同 namespace 或上層 namespace
        visible_ns = self._enclosing_namespaces(ctx.namespace)
        same = [c for c in candidates if c.namespace in visible_ns]
        if len(same) == 1:
            return same[0].full_name
        via_using = [c for c in candidates if c.namespace in ctx.usings]
        if len(via_using) == 1:
            return via_using[0].full_name

        pool = same or via_using or candidates
        chosen = sorted(pool, key=lambda c: c.full_name)[0]
        key = f"{ctx.full_name}->{ref.name}"
        if key not in self._warned_ambiguous:
            self._warned_ambiguous.add(key)
            self.warnings.append(
                f"型別 {ref.name}（於 {ctx.full_name}）有多個候選，暫取 {chosen.full_name}："
                + ", ".join(sorted(c.full_name for c in candidates))
            )
        return chosen.full_name

    @staticmethod
    def _enclosing_namespaces(ns: str) -> set[str]:
        parts = ns.split(".") if ns else []
        return {".".join(parts[:i]) for i in range(len(parts) + 1)}


@dataclass
class Edge:
    source: str
    target: str
    type: str
    via: str | None
    many: bool


def build_edges(decl: TypeDecl, resolver: Resolver, externals: dict[str, dict]) -> list[Edge]:
    """把宣告的原始引用分類成 internal 邊；external 引用寫入 externals。"""
    edges: list[Edge] = []
    for raw in decl.edges:
        for ref in raw.refs:
            if not ref.name or ref.name in BUILTIN_TYPES or ref.name in decl.type_params:
                continue
            target = resolver.resolve(ref, decl)
            if target is None:
                ext = externals.setdefault(ref.qualified, {"name": ref.qualified, "count": 0, "referencedBy": set()})
                ext["count"] += 1
                ext["referencedBy"].add(decl.full_name)
                continue
            if target == decl.full_name:
                continue
            if raw.rel == "base":
                target_kind = resolver.decls[target].kind
                rel = "implements" if target_kind == "interface" and decl.kind != "interface" else "inherits"
            else:
                rel = raw.rel
            edges.append(Edge(decl.full_name, target, rel, raw.via, ref.many))
    return edges


def bfs(entry: str, decls: dict[str, TypeDecl], resolver: Resolver, max_classes: int):
    included: dict[str, int] = {entry: 0}
    order: list[str] = [entry]
    truncated: dict[str, set[str]] = {}
    all_edges: list[Edge] = []
    externals: dict[str, dict] = {}
    queue = deque([entry])

    while queue:
        current = queue.popleft()
        edges = build_edges(decls[current], resolver, externals)
        all_edges.extend(edges)
        for e in sorted(edges, key=lambda e: RELATION_PRIORITY[e.type]):
            if e.target in included:
                continue
            if len(included) < max_classes:
                included[e.target] = included[current] + 1
                order.append(e.target)
                queue.append(e.target)
            else:
                truncated.setdefault(e.target, set()).add(current)

    return included, order, truncated, all_edges, externals


# ---------------------------------------------------------------------------
# 輸出
# ---------------------------------------------------------------------------


def serialize_class(d: TypeDecl, depth: int, members_mode: str) -> dict:
    out: dict = {
        "id": d.full_name,
        "name": d.name,
        "kind": d.kind,
        "namespace": d.namespace,
        "modifiers": d.modifiers,
        "file": d.file,
        "line": d.line,
        "depth": depth,
    }
    if d.partial_files:
        out["partialFiles"] = d.partial_files
    if d.type_params:
        out["typeParameters"] = sorted(d.type_params)
    out.update(d.extra)
    if members_mode != "none" and d.kind not in ("enum", "delegate"):
        members = d.members
        if members_mode == "visible":
            members = [m for m in members if VISIBLE_MODIFIERS & set(m["visibility"].split())]
        out["members"] = members
    return out


def dedupe_relations(edges: list[Edge], included: dict[str, int]) -> list[dict]:
    seen: dict[tuple, dict] = {}
    for e in edges:
        if e.source not in included or e.target not in included:
            continue
        key = (e.source, e.target, e.type)
        rel = seen.get(key)
        if rel is None:
            rel = {"from": e.source, "to": e.target, "type": e.type, "via": []}
            seen[key] = rel
        if e.via and e.via not in rel["via"]:
            rel["via"].append(e.via)
        if e.many:
            rel["multiplicity"] = "many"
    result = []
    for rel in seen.values():
        if not rel["via"]:
            del rel["via"]
        result.append(rel)
    return result


def resolve_entry(entry: str, decls: dict[str, TypeDecl]) -> tuple[str | None, list[str]]:
    if "." in entry:
        matches = [k for k in decls if k == entry or k.endswith("." + entry)]
    else:
        matches = [k for k, d in decls.items() if d.name == entry]
    if len(matches) == 1:
        return matches[0], []
    return None, sorted(matches)


def collect_files(root: Path) -> list[Path]:
    files = []
    for p in root.rglob("*.cs"):
        if any(part in SKIP_DIRS for part in p.relative_to(root).parts[:-1]):
            continue
        files.append(p)
    return sorted(files)


def main() -> int:
    ap = argparse.ArgumentParser(description="從入口類別向下解析 C# 專案，輸出類別結構 JSON metadata。")
    ap.add_argument("--root", required=True, help="C# 專案根目錄")
    ap.add_argument("--entry", required=True, help="入口類別名稱，可含 namespace，例如 Router 或 App.Routing.Router")
    ap.add_argument("--max-classes", type=int, default=HARD_MAX_CLASSES,
                    help=f"納入的專案類別數量上限（預設與硬上限皆為 {HARD_MAX_CLASSES}）")
    ap.add_argument("--max-external", type=int, default=20, help="externalReferences 最多列出筆數（預設 20）")
    ap.add_argument("--members", choices=["visible", "all", "none"], default="visible",
                    help="成員輸出範圍：visible=public/internal/protected（預設）、all、none")
    ap.add_argument("--output", help="輸出 JSON 檔路徑；未指定則輸出到 stdout")
    args = ap.parse_args()

    try:
        import tree_sitter_c_sharp as tscs
        from tree_sitter import Language, Parser
    except ImportError as exc:
        print(
            f"缺少 parser 依賴（{exc}）。請以 `uv run {Path(__file__).name} ...` 執行；"
            f"若無 uv，先執行 `python -m pip install --requirements-from-script {Path(__file__).name}`。",
            file=sys.stderr,
        )
        return 5

    root = Path(args.root).resolve()
    if not root.is_dir():
        print(f"--root 不是有效目錄：{root}", file=sys.stderr)
        return 2
    if args.max_classes < 1:
        print("--max-classes 必須 >= 1", file=sys.stderr)
        return 2

    warnings: list[str] = []
    max_classes = args.max_classes
    if max_classes > HARD_MAX_CLASSES:
        warnings.append(f"--max-classes={max_classes} 超過硬上限，已收斂為 {HARD_MAX_CLASSES}。")
        max_classes = HARD_MAX_CLASSES

    files = collect_files(root)
    if not files:
        print(f"在 {root} 下找不到任何 .cs 檔", file=sys.stderr)
        return 2

    indexer = Indexer(root, Parser(Language(tscs.language())))
    for f in files:
        indexer.index_file(f)
    indexer.finalize()
    warnings.extend(indexer.warnings)
    decls = indexer.decls

    entry_id, candidates = resolve_entry(args.entry, decls)
    if entry_id is None:
        if candidates:
            print(f"入口類別 `{args.entry}` 有多個候選，請改用含 namespace 的名稱：", file=sys.stderr)
            for c in candidates:
                print(f"  - {c}", file=sys.stderr)
            return 4
        names = sorted({d.name for d in decls.values()} | set(decls))
        close = difflib.get_close_matches(args.entry, names, n=5, cutoff=0.5)
        print(f"找不到入口類別 `{args.entry}`。", file=sys.stderr)
        if close:
            print("相近名稱：" + ", ".join(close), file=sys.stderr)
        return 3

    resolver = Resolver(decls, warnings)
    included, order, truncated, edges, externals = bfs(entry_id, decls, resolver, max_classes)

    ext_list = sorted(externals.values(), key=lambda e: (-e["count"], e["name"]))
    omitted_ext = max(0, len(ext_list) - args.max_external)
    ext_list = [
        {"name": e["name"], "count": e["count"], "referencedBy": sorted(e["referencedBy"])}
        for e in ext_list[: max(0, args.max_external)]
    ]

    result = {
        "schemaVersion": 1,
        "entry": entry_id,
        "root": str(root),
        "limits": {
            "maxClasses": max_classes,
            "included": len(included),
            "truncatedCount": len(truncated),
            "projectTypeCount": len(decls),
            "externalOmitted": omitted_ext,
        },
        "classes": [serialize_class(decls[k], included[k], args.members) for k in order],
        "relations": dedupe_relations(edges, included),
        "externalReferences": ext_list,
        "truncated": [
            {
                "id": k,
                "name": decls[k].name,
                "kind": decls[k].kind,
                "file": decls[k].file,
                "line": decls[k].line,
                "reachedFrom": sorted(v),
            }
            for k, v in sorted(truncated.items())
        ],
        "warnings": warnings,
    }

    payload = json.dumps(result, ensure_ascii=False, indent=2)
    if args.output:
        out = Path(args.output)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(payload + "\n", encoding="utf-8")
        print(f"已輸出 {len(included)} 個類別至 {out}", file=sys.stderr)
    else:
        print(payload)
    return 0


if __name__ == "__main__":
    sys.exit(main())
