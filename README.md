# 桌面鬧鐘 AlarmClock

Windows 桌面鬧鐘程式（C# WinForms，編譯成單一 `.exe`，免安裝執行環境）。
可設定指定時間鬧鐘與倒數計時，時間到時會有一隻「會走路的時鐘角色」從螢幕下方出現提醒。

---

## 功能

- **指定時間鬧鐘**：設定 `時:分:秒`，可選「每天」或指定**星期幾**重複，或單次。
- **倒數計時**：可設「幾分幾秒」後提醒，並可**循環倒數**（例如每 30 分提醒起身）。
- **動態提醒彈窗**：紅色時鐘造型角色在螢幕最下方左右走動，停留時間到後定點停住、再自動關閉；提醒文字顯示在時鐘肚子（上限 10 字，超過以 `…` 截斷）。
- **停留時間**：可設彈窗顯示多久後自動關閉，或「不自動關閉（手動）」。
- **響鈴音樂**：預設使用內建 `10 second short music_320k.mp3`，也可自選 wav / mp3 / wma。
- **暫停所有鬧鐘**：總開關，勾選後清單項目全部反灰、不執行。
- **清單管理**：依時間正排／逆排、啟用／停用、刪除；同一時間不重複新增。
- **系統匣常駐**：關閉視窗會縮到系統匣繼續在背景提醒。

---

## 檔案說明

| 檔案 | 說明 | 版控 |
|------|------|------|
| `AlarmClock.cs` | 原始程式碼（全部邏輯與繪圖） | ✅ |
| `AlarmClock.exe` | 編譯後可執行檔，雙擊即用 | ✅ |
| `build.bat` | 一鍵重新編譯並啟動 | ✅ |
| `clock.ico` | 程式圖示（內嵌於 exe） | ✅ |
| `10 second short music_320k.mp3` | 預設響鈴 | ✅ |
| `alarms.txt` | 鬧鐘清單（程式自動產生／儲存） | ❌ 已忽略 |
| `settings.txt` | 暫停狀態等設定（自動產生） | ❌ 已忽略 |

> 預設響鈴是以「exe 同資料夾」定位，搬移 exe 時請連同 mp3 一起帶走。

---

## 使用方式

直接雙擊 `AlarmClock.exe`。設定時間或倒數、輸入提醒項目、（可選）選響鈴音樂，按「新增鬧鐘」或「倒數新增」即可。
可按「測試動態」立即預覽提醒彈窗效果。

開機自動啟動：按 `Win + R` 輸入 `shell:startup`，把 `AlarmClock.exe` 的捷徑放進去。

---

## 修改與重新編譯

**重要：`AlarmClock.cs` 只是原始碼，改完一定要重新編譯，`AlarmClock.exe` 才會變。**

最簡單：改完 `AlarmClock.cs` 後，**雙擊 `build.bat`**（會自動關閉舊程式、重編、再啟動）。

手動編譯指令：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /win32icon:clock.ico /out:AlarmClock.exe /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /optimize+ AlarmClock.cs
```

### 常見可調整處（都在 `AlarmClock.cs` 的 `DrawClock()`）

- 時鐘大小：`ClockR`
- 鈴鐺大小：`float rb = r * 0.38f`
- 腿長：`lendY = cy + r * 1.02f`（與 `ly` 的差）
- 腳掌大小：`g.FillEllipse(redDark, ..., 28, 15)` 的寬×高
- 移動速度：`AlarmPopup` 內 `private int dx`
- 肚子文字字數上限：`if (disp.Length > 10)`

---

## 版本控制

純本地 Git（無遠端、不上傳）。提交時請說明變更內容即可。
