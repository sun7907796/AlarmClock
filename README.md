# 桌面鬧鐘 AlarmClock

Windows 桌面鬧鐘程式（C# WinForms，編譯成單一 `.exe`，使用系統內建 .NET Framework，免安裝執行環境）。
設定時間或倒數,時間到時會有一隻**會走路的角色**從螢幕下方出現,搭配對話框顯示提醒文字,可同步推播到 **LINE**。

專案：<https://github.com/sun7907796/AlarmClock>

---

## 功能

- **指定時間鬧鐘**：設定 `時:分:秒`,可選「每天」或指定**星期幾**重複,或單次。
- **倒數計時 / 循環**：可設「幾分幾秒」後提醒,並可**循環倒數**(例如每 30 分提醒起身)。
- **動態提醒彈窗**：角色在螢幕最下方左右走動(碰邊反彈、依方向翻面),上方對話框永久顯示時間與提醒文字;停留時間到後定點停住、再自動關閉。
- **自訂動態圖片**：可用內建時鐘造型,或選自己的圖;支援**橫向多格 sprite 圖**自動切幀成走路動畫(自動去除透明/半透明背景)。
- **停留時間**：可設彈窗顯示多久後自動關閉,或「不自動關閉(手動)」。
- **響鈴音樂**：預設內建鈴聲,可自選 wav / mp3 / wma,可試聽。
- **提醒項目下拉**：常用提醒可自行**新增/刪除**選項。
- **清單管理**：時間正排/逆排、**雙擊編輯**、啟用/停用、刪除;同一時間不重複新增;停用或暫停中的項目顯示為灰色。
- **暫停所有鬧鐘**：總開關,一鍵暫停/恢復。
- **LINE 群組通知**：鬧鐘響時同步推播到 LINE 群組或個人(Messaging API push)。
- **開機自動啟動**：勾選後開機自動以系統匣待命(`/min`)。
- **單一執行個體**：重複開啟只會喚回既有視窗,避免多開造成設定混亂。
- **系統匣常駐**：關閉視窗會縮到系統匣繼續在背景提醒。

---

## 下載與執行

1. 到 [Releases](https://github.com/sun7907796/AlarmClock/releases) 下載 zip(或直接下載整個專案)。
2. 解壓縮後,**保持所有檔案在同一資料夾**(exe 會用到同資料夾的鈴聲與圖片)。
3. 雙擊 **`AlarmClock.exe`** 即可,免安裝。

---

## 使用方式

設定時間或倒數 → 輸入/選擇提醒項目 →(可選)選響鈴音樂與動態圖片 → 按「新增鬧鐘」或「倒數新增」。
可按「測試動態UI」立即預覽彈窗效果;程式內建「使用說明」按鈕。

---

## 檔案說明

| 檔案 | 說明 | 版控 |
|------|------|------|
| `AlarmClock.cs` | 原始程式碼 | ✅ |
| `AlarmClock.exe` | 可執行檔,雙擊即用 | ✅ |
| `build.bat` | 一鍵重新編譯並啟動 | ✅ |
| `clock.ico` | 程式圖示(內嵌於 exe) | ✅ |
| `10 second short music_320k.mp3` | 預設響鈴 | ✅ |
| `虎斑貓.png` / `賓士貓*.png` | 動態UI走路動圖(橫向 sprite) | ✅ |
| `alarms.txt` | 鬧鐘清單(程式自動儲存) | ❌ 已忽略 |
| `settings.txt` | 暫停/LINE/動態圖片等設定(含 LINE token) | ❌ 已忽略 |
| `reminders.txt` | 提醒項目下拉選項 | ❌ 已忽略 |

> 執行時產生的 `alarms.txt`、`settings.txt`、`reminders.txt` 不納入版控(避免 LINE token 等個資外流)。

---

## LINE 通知設定(選用)

1. LINE Developers 建立 Messaging API channel,取得 **Channel access token**。
2. 取得目標 ID(群組 `C...` / 個人 `U...`;需先把 Bot 加為好友/邀入群組)。
3. 程式內「LINE 通知設定…」填入 token 與 ID,按「測試發送」確認,勾選啟用。

---

## 修改與重新編譯

**`AlarmClock.cs` 只是原始碼,改完一定要重新編譯,`AlarmClock.exe` 才會變。**
最簡單:改完後**雙擊 `build.bat`**(自動關閉舊程式、重編、再啟動)。

手動編譯指令：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /win32icon:clock.ico /out:AlarmClock.exe /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /optimize+ AlarmClock.cs
```

---

## 版本控制

Git 專案,遠端於 GitHub。提交後以 `git push` 同步。
