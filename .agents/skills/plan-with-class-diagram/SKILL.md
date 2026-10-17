---
name: plan-with-class-diagram
description: "Use when planning code changes with a Mermaid class diagram before implementation. Propose the class organization, wait for the user's approval, then implement in dependency order."
---


# SOP

## Phase 1 —— 理解需求與現有結構

1. READ 讀取用戶需求、相關程式碼、專案慣例與既種類別。
2. THINK 拆解待開發功能，收斂主要類別、介面及其關係；需要判斷類別範圍、職責、關係語意或依賴順序時，READ `rules/類別圖設計判準.md`，再依已載入判準完成設計。

## Phase 2 —— 產出類別圖提案

1. WRITE 產出類別圖提案時，先讀取 `templates/class-diagram-proposal.md` 與 `templates/class-diagram-proposal.example.md`，依骨架複製 Markdown 結構、參考範例填入 Mermaid 類別圖及設計說明，並寫入 `specs/plan/<NN>-<本次計劃名稱>/class-diagram-proposal.md`；`NN` 為既有最大序號加一並補零至兩位數，沒有既有計劃時使用 `01`，不覆寫既有提案。
2. WRITE 詢問用戶是否同意此設計並進入開發；未確認前停止，不寫程式。

## Phase 3 —— 依圖施工

1. THINK 依類別圖推導實作順序：先實作被依賴的類別，再實作依賴方與整合層。
2. WRITE 依序實作類別圖中的元件，保持與提案一致。
3. READ 檢查實作是否對齊類別圖；若有必要偏差，先向用戶說明再調整。