---
name: plan-with-class-diagram
description: "Use when planning code changes with a Mermaid class diagram before implementation. Propose the class organization, wait for the user's approval, then implement in dependency order."
---


# SOP

## Phase 1 —— 理解需求與現有結構

1. DELEGATE 若目標為既有 C# 專案，需要先掌握既有類別架構，依用戶需求選定入口類別，執行 `uv run scripts/csharp_class_graph.py --root <專案根目錄> --entry <入口類別> --max-classes 30` 取得上限內專案類別的 JSON metadata（`classes` 的成員簽章與檔案位置、`relations` 的類別關係；BCL 與外部套件型別另列於 `externalReferences`，不佔名額），作為後續挑選閱讀範圍的抽象地圖；若 `truncated` 中有與需求相關的類別，改以該類別為入口再執行一次。依賴已宣告於 script 檔頭，僅在 `uv` 不可用時改用 `python -m pip install --requirements-from-script scripts/csharp_class_graph.py` 後執行 `python scripts/csharp_class_graph.py ...`。
2. READ 讀取用戶需求與專案慣例，並依上一步 metadata 的關係與檔案位置，挑選與需求相關的類別深入閱讀原始碼；非 C# 專案或無既有程式碼時，直接讀取相關程式碼與既有類別。
3. THINK 拆解待開發功能，收斂主要類別、介面及其關係；需要判斷類別範圍、職責、關係語意或依賴順序時，READ `rules/類別圖設計判準.md`，再依已載入判準完成設計。

## Phase 2 —— 產出類別圖提案

1. WRITE 產出類別圖提案時，先讀取 `templates/class-diagram-proposal.md` 與 `templates/class-diagram-proposal.example.md`，依骨架複製 Markdown 結構、參考範例填入 Mermaid 類別圖及設計說明，並寫入 `specs/plan/<NN>-<本次計劃名稱>/class-diagram-proposal.md`；`NN` 為既有最大序號加一並補零至兩位數，沒有既有計劃時使用 `01`，不覆寫既有提案。
2. WRITE 詢問用戶是否同意此設計並進入開發；未確認前停止，不寫程式。

## Phase 3 —— 依圖施工

1. THINK 依類別圖推導實作順序：先實作被依賴的類別，再實作依賴方與整合層。
2. WRITE 依序實作類別圖中的元件，保持與提案一致。
3. READ 檢查實作是否對齊類別圖；若有必要偏差，先向用戶說明再調整。