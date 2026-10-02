# 紅色粒子射線

## 雙門解謎成功關閉射線

`SampleScene` 的 **doubledoormachine → machine** 已掛上 **Puzzle Solved Ray Shutdown**，並連結目前的 RedParticleRay。

- **雙門解謎控制器**：指定 DualDoorPuzzleController；未指定時找同一物件上的控制器。
- **成功後關閉的射線**：可用清單的 `+` 或 Size 添加多條射線，拖入具有 RedParticleRay 的物件。
- **亮度淡出時間**：預設 0.25 秒，可調整快速變暗所需時間；0 直接關閉。

成功條件沿用雙門控制器：兩扇門都開啟，且機關已永久停止。成功後各射線會從自己的目前 Brightness 同時下降到 0，再關閉射線 GameObject（即死判定也隨之停止）。只執行一次，之後條件變化不會重新開啟射線。請將此腳本放在持續啟用的機關物件上。

開啟 `Assets/Scenes/SampleScene.unity`，在 Hierarchy 選取 `RedParticleRay`。所有外觀設定都在 Inspector 的 **Red Particle Ray** 元件；編輯模式與 Play 模式都會即時更新。

| 欄位 | 調整內容 | 場景預設值 |
| --- | --- | --- |
| Brightness | 亮暗，0 關閉，1 標準亮度，最高 10 | 2 |
| Length | 射線長度，單位與場景世界單位相同 | 5 |
| Width | 射線核心寬度，外圍光暈會較寬 | 0.15 |
| Max Length | 長度上限；Length 超過上限時會被限制 | 10 |
| Ray Color | 射線顏色與透明度 | 紅色 |
| Glow Width | 光暈寬度相對於 Width 的倍數 | 3 |
| Sorting Order | 關閉 Auto Depth Sorting 時的固定排序值 | 20 |

## 玩家與雷射的前後遮擋

在 **Depth Sorting / 前後遮擋** 區塊，預設開啟 **Auto Depth Sorting**，雷射與玩家共用場景 `AutoDepthSortManager` 的鏡頭、Sorting Layer 和深度精細度。玩家比雷射起點更靠近鏡頭時，玩家會蓋住雷射；較遠時雷射會顯示在玩家前方。

**Depth Sorting Offset** 預設為 0，用來微調同深度附近的繪製順序（正值偏前、負值偏後）。改變雷射的 Transform Position 可調整它在場景中的前後位置。關閉 Auto Depth Sorting 時改用固定 Sorting Order。自動模式需要場景有啟用的 AutoDepthSortManager。

整條粒子射線依起點深度排序；若將射線旋轉成跨越多個深度的方向，這個排序不會將同一條射線切成前後段。

## 光暈呼吸效果

在相同元件的 **Glow Breathing / 光暈呼吸** 區塊調整，按 Play 即可看到效果：

| 欄位 | 調整內容 | 場景預設值 |
| --- | --- | --- |
| Enable Glow Breathing | 啟用光暈呼吸；關閉時使用固定的 Glow Width | 開啟 |
| Min Glow Width | 呼吸時最小光暈寬度倍數 | 2 |
| Max Glow Width | 呼吸時最大光暈寬度倍數 | 4 |
| Breathing Period | 由小變大再變小的完整週期，秒數越大越慢 | 3 秒 |

預設會在 1.5 秒內從 2 平滑增加至 4，再花 1.5 秒減少至 2，循環播放。變化同時套用至粒子尺寸與 Shader，核心寬度與射線長度保持固定。呼吸使用遊戲時間，暫停遊戲時間時也會暫停呼吸。停止播放時顯示固定 Glow Width；播放中的呼吸寬度以程式計算，不會覆寫 Inspector 的 Glow Width 設定值。

使用 Transform 的 Position 移動起點、Rotation 旋轉方向。射線從物件起點沿本地 +X（紅色軸）延伸；2D 場景通常調整 Rotation Z。請使用 Length 與 Width 調整大小，程式會抵消一般 Transform 縮放，以維持世界單位的長度上限。

在 Scene 視窗開啟 Gizmos 並選取物件：紅色標示代表起點、目前終點與方向，橘色圓圈代表 Max Length 終點。選取物件後按 F 可以定位。

目前效果以 Particle System 的單一網格粒子形成連續射線，使用 URP 加色混合 Shader 產生紅色核心和光暈。不需要手動修改程式產生的粒子子物件。亮度高時，若希望有更明顯的畫面泛光，可搭配場景的 Bloom。

Max Length 是手動長度限制；目前不包含碰撞截斷或解謎觸發。可複製場景中的 RedParticleRay 物件以建立多條射線。Play 模式中的參數修改一般不會在停止播放後保留，永久設定請在停止播放時調整。

## 雷射即死與重生點

SampleScene 的 RedParticleRay 已掛上 **Laser Death Trigger**。玩家的 3D Collider 接觸雷射核心時，會透過 PlayerHealth.ForceDeath 立即歸零血量、停用移動並播放既有死亡畫面，等待 3 秒後復活。呼吸光暈不會改變碰觸半徑，亮度為 0、透明度為 0、長度為 0 或停用雷射元件時不會致死。

**玩家碰撞圖層** 必須包含玩家 Collider 的圖層；**額外碰觸半徑** 可增加核心判定範圍。判定使用沿射線的 3D 膠囊範圍，端點會延伸一個判定半徑。玩家 Collider 必須位於 PlayerHealth 本身、子物件或同一 Rigidbody 下。其他雷射可透過 Add Component 加上 LaserDeathTrigger。

選取 **player → Player Health → 死亡後重生點**，拖入代表重生位置的場景物件。SampleScene 已連結 **PlayerRespawnPoint**，預設位於玩家開場位置；移動它即可調整復活位置。請將它放在雷射範圍外。未指定重生點時回到玩家開場位置，所有死亡來源都使用這個設定。重生會恢復滿血、移動並清除 Rigidbody 速度。
