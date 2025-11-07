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


## 使用 Cline 調試 Everything MCP Server

以下示範如何透過 [Cline](https://github.com/cline/cline) MCP 用戶端與此伺服器建立連線並進行調試：

1. **安裝並啟動伺服器**：先依照專案的開發流程啟動 Everything MCP Server（預設監聽 `http://localhost:3001`）。
2. **安裝 Cline**：在 Visual Studio Code 安裝「Cline」擴充功能，或在支援 Cline 的環境依照官方文件完成安裝。
3. **建立設定檔**：在工作目錄新增 `.cline/settings.json`（若已存在則更新），加入以下內容宣告自訂 MCP Server：

   ```json
   {
     "mcpServers": {
       "everything-mcp-server": {
         "type": "http",
         "url": "http://localhost:3001/"
       }
     }
   }
   ```

   - `everything-mcp-server` 可依需求改為其他識別名稱。
   - `url` 須對應實際執行中的伺服器位址與埠號。
4. **啟動 Cline 並選擇伺服器**：在 Cline 面板中選擇剛新增的 `everything-mcp-server`，首次連線時 Cline 會自動送出 `initialize` 要求並在回應中取得 `Mcp-Session-Id`。
5. **調試與測試**：
   - 透過 Cline 的工具列表可呼叫 `EverythingServer/Tools/` 中定義的工具。
   - 於資源頁籤可瀏覽 `EverythingServer/Resources/` 提供的內容。
   - 若需要長時連線測試，請留意 Cline 的輸出視窗以取得伺服器端的訂閱與通知訊息。

完成上述設定後，即可利用 Cline 進行互動式調試，快速驗證各項工具、提示詞與資源的行為。


## 取得 Mcp-Session-Id

1. 啟動 Everything MCP Server（預設監聽 `http://localhost:3001`）。
2. 對根路徑送出一次 **POST** `initialize` 請求，並在標頭中同時宣告 `Accept: application/json, text/event-stream` 以及 `Content-Type: application/json`。請確認請求主體包含合法的 JSON-RPC `initialize` 內容，若以 POST 但未附帶 JSON 內文，伺服器會回傳 `Bad Request: Mcp-Session-Id header is required` 或 JSON 解析例外（`The input does not contain any JSON tokens`）。**首次請求請勿帶入 `Mcp-Session-Id` 標頭，否則會被視為查詢既有會話。**
3. 伺服器會在回應標頭中加入 `Mcp-Session-Id`，將該值保存下來即可。

以下示範使用 `curl` 取得 Session Id：

```bash
curl -i -X POST http://localhost:3001/ \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
          "clientInfo": { "name": "RestClient", "version": "0.1.0" },
          "capabilities": {},
          "protocolVersion": "2025-06-18"
        }
      }'
```

在命令輸出的回應標頭中，可找到類似以下內容：

```
Mcp-Session-Id: ZwwM0VFEtKNOMBsP8D2VzQ
```

後續的 MCP 呼叫需於 HTTP 標頭中帶入該 `Mcp-Session-Id` 才能維持相同的會話。

### 常見錯誤排除

#### 使用 Postman 時 Headers 設定一定要勾上 Content-Length
如不勾上，Postman UI 看起來有 Body，但 Postman 其實沒有把 Body 送出去。