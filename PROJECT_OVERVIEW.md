# Everything MCP Server 概覽

Everything MCP Server 是一個使用 .NET 建置的示範型 Model Context Protocol (MCP) 伺服器，展示如何在一個專案中整合各種 MCP 功能。專案的入口點位於 `EverythingServer/Program.cs`，透過 `AddMcpServer` 擴充方法建立伺服器並註冊多種服務與處理程序。

## 主要能力

- **工具 (Tools)**：在 `EverythingServer/Tools/` 中提供多個 MCP 工具範例，例如基本的加法工具 (`AddTool`)、回音測試 (`EchoTool`)、長時間執行的工作 (`LongRunningTool`)、環境變數輸出 (`PrintEnvTool`)、示範 LLM 呼叫 (`SampleLlmTool`) 以及回傳 Base64 影像的 `TinyImageTool`。
- **提示詞 (Prompts)**：`EverythingServer/Prompts/` 定義了簡單與複雜兩種提示詞類型，用於展示如何在 MCP 內提供靜態內容或帶參數的對話提示。
- **資源 (Resources)**：`EverythingServer/Resources/` 提供靜態資源與模板資源，結合 `ResourceGenerator` 產生測試資料，並透過 MCP API 以文字或二進位格式提供內容。
- **訂閱與通知**：伺服器在 `Program.cs` 中實作資源訂閱與解除訂閱的處理程序，並搭配 `SubscriptionMessageSender` 與 `LoggingUpdateMessageSender` 兩個背景服務定期推送資源更新與日誌級別相關通知。
- **自動補全**：範例 `WithCompleteHandler` 展示如何提供工具參數的自動補全建議。

## 執行方式概述

專案使用 `EverythingServer.csproj` 與 `EverythingServer.sln` 管理，透過 ASP.NET Core 建立 HTTP 傳輸層。啟動後，即可透過 MCP 相容的客戶端連線並測試上述工具、資源、提示詞與訂閱通知等功能。
