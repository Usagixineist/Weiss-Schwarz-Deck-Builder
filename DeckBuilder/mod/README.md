# Deck Sync 插件（WeissSchwarzDeckSync）

DeckBuilder 保存的卡组只写 `StreamingAssets\Decks`，而游戏读的是 PlayerPrefs（Windows 下即注册表
`HKCU\Software\Blake Thoennes\Weiss Schwarz`），两边原本互不相通。这个 BepInEx 插件把两个存储做成双向同步：

- 在 DeckBuilder 里“保存”的卡组 → 自动出现在游戏的卡组列表里；
- 在游戏里新建或修改的卡组 → 自动导出成 `StreamingAssets\Decks\<卡组名>.txt`，DeckBuilder 点“刷新卡组”即可看到。

所有源码、构建脚本与产物都放在 `DeckBuilder\mod` 下，仓库其它目录（尤其是 `wsmod\`）不需要任何改动，
所以整个 `DeckBuilder` 目录可以单独打包。

## 构建与安装

```powershell
# 构建并安装到 BepInEx\plugins\WeissSchwarzDeckSync（只新增这一个目录）
powershell -NoProfile -ExecutionPolicy Bypass -File DeckBuilder\mod\build.ps1

# 只构建，不安装
powershell -NoProfile -ExecutionPolicy Bypass -File DeckBuilder\mod\build.ps1 -NoInstall
```

- 产物：`DeckBuilder\mod\plugin\WeissSchwarzDeckSync.dll`
- 安装：复制到 `BepInEx\plugins\WeissSchwarzDeckSync\WeissSchwarzDeckSync.dll`
- 反安装：删掉 `BepInEx\plugins\WeissSchwarzDeckSync` 即可，注册表里的卡组不受影响

## 配置

首次进入游戏会生成 `BepInEx\config\ws.deckbuilder.decksync.cfg`，可随时修改（下一轮同步生效）：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| Enabled | true | 总开关 |
| SyncToGame | true | 文件 → 游戏 |
| SyncFromGame | true | 游戏 → 文件 |
| IncludeDefaultDecks | false | 连 `DefaultDecks` 里的预设/AI 卡组一起导出 |
| RequireFullDeck | true | 只有正好 50 张的卡组才写进游戏 |
| ValidateCardCodes | true | 写进游戏前检查每一行卡号 |
| PollSeconds | 15 | 自动同步间隔（最小 3 秒） |
| SyncOnSceneLoad | true | 每次切换场景（回主菜单等）同步一次 |
| RefreshDeckSelector | true | 导入后若卡组界面正开着，立刻重建列表 |
| Verbose | false | 把没有变化的卡组也写进日志 |
| SkipDeckNames | Placeholder | 完全忽略的卡组名，逗号分隔 |
| Hotkey | F9 | 手动同步一次 |
| StateFile | deck-sync-state.txt | 同步基线文件名（相对插件目录） |

## 同步规则

- 身份是卡组名：`StreamingAssets\Decks\<名字>.txt` ↔ PlayerPrefs `Deck_<名字>`。
- 只扫描 `Decks` 一层（不递归）：DeckBuilder 的“暂存”写在同层的
  `StreamingAssets\DeckDrafts`，所以暂存卡组永远不会被同步进游戏。
- 基线文件记录上一轮两侧内容的哈希（FNV-1a）：
  - 只有一侧变了 → 同步这一侧；
  - 两侧都变了 → 比时间（文件修改时间 vs 游戏 `Date_`，1 分钟容差），新的胜；
  - 内容一致 → 什么都不做。
- 只存在一侧：文件侧 → 导入游戏；游戏侧 → 导出成文件。
- 两边删除会互相同步：在游戏里删掉卡组后，镜像文件不会被当成新卡组重新导入，而是连
  镜像文件一起删掉；在 DeckBuilder 的“查看卡组”或资源管理器里删掉 `Decks` 里的文件，
  只要游戏卡组仍是上次同步的内容，也会从 PlayerPrefs 删除 `Deck_`、`Date_`、`Sleeve_`
  与 `DeckNames` 条目（删除游戏卡组需要 `SyncToGame`）。如果游戏里又改过这副卡组，
  则按冲突规则处理，不会误删新内容。
- 写入游戏时完全照 `DeckSavingAndLoading.SaveDeck` 的格式维护：
  `Deck_<名字>` = `卡号|卡号|`、`DeckNames` = `名字|名字|`、
  `Date_<名字>` = `HH:mm  MM/dd/yyyy`、`Sleeve_<名字>` 只在新建时补空值，
  不会覆盖玩家在游戏里选好的卡套。
- 名字含 `|` 的卡组无法写进 `DeckNames`，会被跳过并记一条警告；导出时文件名里的
  非法字符用 `_` 代替。
- 卡组张数不是 50（默认设置下）或含有无法识别的卡号时不会写进游戏，日志会给出原因，
  每一份内容只警告一次。

## 日志

`BepInEx\LogOutput.log` 里搜索 `[DeckSync]`：

```
[DeckSync] deck folder: E:/Weiss Schwarz CH-0.6/Weiss Schwarz_Data/StreamingAssets\Decks
[DeckSync] exported from the game: 8Y黄图 (50 cards, 8Y黄图.txt)
[DeckSync] imported into the game: ZZSyncTest (50 cards, ZZSyncTest.txt)
[DeckSync] startup: imported 1, exported 2
```

## 备份与恢复（建议先做一次）

```powershell
reg export "HKCU\Software\Blake Thoennes\Weiss Schwarz" ws-decks.reg
reg import ws-decks.reg
```

## 已知边界

- 只支持 Windows：Android 版的卡组存在 `persistentDataPath/SaveData/*.txt`，插件检测到非 Windows 会自动停用。
- 游戏运行中 Unity 的 PlayerPrefs 有内存缓存，从外部直接改注册表不会被游戏感知；要手改请先退出游戏。
  DeckBuilder 的“删除”会直接清理注册表，游戏开着时插件会在下一轮同步按“文件已删除”再走一次
  PlayerPrefs 删除并保存，最终以退出游戏后的状态为准。
- 导出到 `Decks` 的卡组同样会出现在游戏自己的“导入卡组”列表里（这是游戏读同一个目录导致的）。
- 插件只在导入后尝试刷新已经打开的 `DeckSelector`；其它界面需要重新打开卡组列表才会看到新卡组。
