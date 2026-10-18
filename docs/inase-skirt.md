# Inase（a08）的裙子：游戏里怎么动的，有没有比 Magica Cloth 更好的做法（10-05）

用户 10-05："inase的裙子再看一下，先看游戏里的物理是怎么控制的，是否有比magic cloth处理更好的方式"。

## 结论

1. **游戏里根本没有物理**（战斗里的角色、敌人、舞台、技能全都查过：`docs/roe-battle-physics.md`）。
   - a08 的战斗包和展示包里只有动画组件（Animator），没有任何物理组件。
   - 裙子的 34 根骨在她全部 10 个游戏动作里都是逐帧手 K 的。
   - Magica Cloth 2 只在大厅里用：头发、挂件、裙子上的链子，都是一条条独立的骨链。
2. **手 K 的裙子是"甩"出来的，不是"跟着腿"摆出来的。**
   - 拿腿的姿势去预测裙子：站着的动作能大致预测（误差 5–11°）；技能里完全预测不了，所有模型都在 44–58°，和裙子一动不动差不多。
   - 剑星裙子 Control Rig 那种规则（大腿抬起来才把裙片推开）也一样。所以剑星的做法帮不了 Inase。
3. **拿她自己的技能当标准答案比，现有做法都一样远。**
   - 把游戏的裙子关键帧去掉、交给各个方案去动，和游戏手 K 的逐帧比方向：
     - 我们的骨骼布料 62.6°；
     - 真的 Magica Cloth 2 61.4°；
     - 只跟着腿、不模拟 61.3°。
   - 视频里看得很清楚：游戏里技能中的长裙片大幅甩开、往后飘；所有物理版本都贴着腿往下垂。
   - 差别不在用哪个插件，而在目标：我们的参数是照"自然下垂、踢腿不把裙子甩飞"调的（10-03），游戏动画师要的是大幅度的飘动。
4. **更好的做法**见第 4 节，要你定方向：要"像游戏那样甩得开"，还是保持现在的"自然下垂"，或者两种都留着切换。

## 1. 游戏里怎么控制的

- 2026-10-02 和 10-05 两次扫包（`scripts/riseoferos/roe_physics_survey.py`，在 ripper_tpose，只读；文档 ripper_tpose `docs/roe-game-physics.md`）：
  - 战斗、展示用的 `chara_armor_pc_*` 共 264 个包，一个物理组件都没有；
  - 大厅的 `meta_armor_pc_*` 有 Magica Cloth 2，但全是头发、挂件、链子的 BoneCloth（独立骨链，"头发"那一套参数），裙片本身不在里面。
- a08 的 10 个游戏动作（`chara_armor_pc_a08_hd & ld_ld_prelude.ab` 7 个，`chara_armor_pc_a08_hd & ld_hd.ab` 3 个），**每个都给 34 根裙骨打了关键帧**：

| 动作 | 长度 | 裙骨转得最多的（相对静止姿势） |
|---|---|---|
| idle_01 站架 | 2.0 s | Skirt_L_00 27°、Skirt_R_16 24° |
| idle_02 待机 | 5.0 s | Skirt_L_00 22° |
| react_01 / react_02 反应 | 2.6 / 2.5 s | 32° / 35° |
| hurt 受击 | 1.4 s | Skirt_R_00 67° |
| skill_01 技能 1 | 5.0 s | 后片 Skirt_M_00 111°（翻到后面）、Skirt_R_11 97° |
| skill_02 技能 2 | 4.4 s | 后片 82°、Skirt_R_00 70° |
| skill_03 超必杀 | 5.0 s | 后片 88°、Skirt_R_00 77° |
| die 倒地 / rip 躺地 | 2.5 / 0.5 s | 62° / 34° |

- 裙子上的 26 根链子骨（`Skirt_Chain_*`）在战斗动作里不动（大厅里才由 Magica 甩）。

## 2. 关键帧和腿是什么关系

`tools/skirt_key_models.py`：从游戏包里把关键帧解出来（ripper_tpose 的 `decode_roe_clip.py`），用大腿相对骨盆的转动去预测每根裙骨的转动。
模型只用一部分动作学，到没见过的动作上去比：把裙子按预测摆好，每根骨的方向和关键帧比（骨盆坐标系里的夹角，均方根）。

- 模型：
  - 静止（裙子不动）；
  - 线性；
  - 分段线性（每个方向一个"只在一侧起作用"的斜坡，就是剑星裙子 rig 的写法）；
  - 最近邻（找关键帧里腿的姿势最像的 6 帧，取它们的裙子）。
- 输入也试了加"骨盆相对重力的方向"和"大腿的角速度"。

每次留一个动作出来检验（单位：度）：

| 模型 | idle_01 | react_01 | skill_01 | skill_03 | hurt | idle_02 | react_02 | skill_02 | 平均 |
|---|---|---|---|---|---|---|---|---|---|
| 最近邻（姿势 + 速度） | 4.9 | 16.2 | 57.8 | 51.2 | 36.1 | 2.6 | 29.0 | 46.1 | 30.5 |
| 线性 | 11.1 | 12.8 | 55.7 | 51.7 | 34.6 | 11.4 | 24.8 | 45.3 | 30.9 |
| 分段线性（剑星式） | 10.2 | 15.1 | 54.9 | 52.5 | 34.1 | 11.5 | 35.0 | 47.5 | 32.6 |
| 静止（不动） | 24.1 | 18.7 | 52.1 | 52.7 | 40.1 | 17.3 | 18.3 | 50.7 | 34.3 |

- 站着的动作（待机、站架、反应）裙子大致跟着腿，能预测。格斗里现在的"裙子跟着腿走"（`RoeSkirtRig`，10-02 起）就是做这一部分。
- 技能、受击里的裙子和腿的姿势基本没关系，是跟着身体的转身、冲刺甩出来的：任何"按姿势算"的规则都学不到。

## 3. 拿游戏手 K 当标准答案比

在她自己的技能动作上，各个方案和游戏手 K 的放在一起：

- 新加的检查开关 `FighterRig.IgnoreSkirtKeys`：游戏动作里也不用裙子的关键帧，裙子照动捕动作那样处理（跟着腿的基础姿势 + 物理）；
  同时每帧记下关键帧，量最后的裙子差多少（每根裙骨在骨盆坐标系里的方向夹角，均方根）。
- 演示脚本 `-roeScript skills`：站架 1 秒，技能 1，再技能 2（共 12.6 秒）。编辑器录像（`RoeClothDemo.Run`）和播放模式录像（`RoeClothPlayDemo.Run`，真 Magica 只在播放模式里动）都认，`-roeSkirtKeys off` 打开上面的开关。
- 新的对比列 `legs`：只跟着腿（`RoeSkirtRig` 的基础姿势），不模拟。

视频 `out\a08_skirt_vs_keys.mp4`：左到右：游戏手 K（标准答案） / Magica Cloth 2 / 我们的骨骼布料 / 只跟着腿，后三列都不用关键帧。技能 1、技能 2 的 9.4 秒里：

| 方案 | 和游戏手 K 差多少（均方根） | 最大 |
|---|---|---|
| 我们的骨骼布料 | 62.6° | 178° |
| Magica Cloth 2（长裙片 MeshCloth，其余 BoneCloth） | 61.4°* | 176° |
| 只跟着腿，不模拟 | 61.3° | 176° |

\* Magica 的长裙片是网格布，模拟的是顶点，骨头停在基础姿势上，所以这个数主要反映基础姿势；看视频比较准。

看到的：

- 技能 1 收招（5.2 秒）、技能 2 转身（9.0 秒）：游戏里长裙片往外、往后大幅飘开；三个方案的裙片都贴着腿垂。
- 技能 1 跪地伸腿（3.0 秒）：游戏里裙片铺开搭在伸出去的腿上；物理版本基本竖直挂着。
- 站定以后（10.4 秒）四列差不多；Magica 的两片前裙分得更开。
- 三个方案的数字几乎一样，说明物理在这里几乎没改变什么：现在的参数让裙子很"稳"，加上 10-03 的"自然下垂"，跟着身体甩的那部分被压掉了。

## 4. 有没有比 Magica 更好的做法

| 做法 | 是什么 | 对 Inase 有没有用 |
|---|---|---|
| A. 拿游戏关键帧当目标调物理（推荐） | 用第 3 节的标准答案自动搜参数：阻尼、跟随身体移动和转身的惯性、离心力、重力、恢复力、自然下垂的强度。用一部分游戏动作调，留几个检验，看视频 | 直接针对现在的差距（甩不开）。代价：甩得开就更容易飘起来、穿到腿里，和 10-03"自然下垂"的方向相反，可以做成两套参数切换 |
| B. 像游戏那样逐个动作烘成关键帧 | 每个动捕动作离线用更慢、更准的模拟跑一遍，存成裙子的关键帧，打斗时照关键帧播（和游戏一样，过渡时混合） | 结果稳定、不耗运行时间、坏帧能手修；但要一条新的流水线，而且离线模拟的效果仍取决于 A 的参数。A 做好后再考虑 |
| C. 剑星式：按大腿驱动裙片的 rig | 规则或拟合出来的"腿抬多少、裙片转多少" | 第 2 节测过：技能里预测不了；站姿的部分 `RoeSkirtRig` 已经在做 |
| D. 换成网格布（Magica MeshCloth 或自己写） | 模拟裙片的顶点而不是骨头，能折叠 | 10-04 看到折叠和搭在腿上更好看，但和游戏的差距一样大；Magica 不进仓库、只在播放模式里跑 |

游戏自己的动作（技能、受击、出场、胜利）现在已经直接用游戏的裙子关键帧，不受上面任何一种影响。

## 5. 命令

```powershell
# 关键帧和腿的关系（第 2 节）
python tools\skirt_key_models.py a08
# 游戏手 K | 骨骼布料 | 只跟着腿 | Magica（第 3 节）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','a08','-roeCloths','off','-roeScript','skills','-roeOut','E:\code\othercode\roe_fighter_unity\_work\a08_skills_keys'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','a08','-roeCloths','magica_style,legs','-roeScript','skills','-roeSkirtKeys','off','-roeOut','E:\code\othercode\roe_fighter_unity\_work\a08_skills_nokeys'
.\tools\unity_batch.ps1 -NoQuit -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run -Graphics -Extra '-roeChars','a08','-roeCloths','magica','-roeScript','skills','-roeSkirtKeys','off','-roeOut','E:\code\othercode\roe_fighter_unity\_work\a08_skills_mc2'
# 几次录像的镜头合到一个文件夹，改名成 keys / mc2_nokeys / ours_nokeys / legs_nokeys
python tools\merge_takes.py _work\a08_skills_all _work\a08_skills_keys:a08_off=keys _work\a08_skills_mc2:a08_magica=mc2_nokeys _work\a08_skills_nokeys:a08_magica_style=ours_nokeys _work\a08_skills_nokeys:a08_legs=legs_nokeys
python tools\cloth_demo_video.py _work\a08_skills_all out\a08_skirt_vs_keys.mp4 --chars a08 --variants keys,mc2_nokeys,ours_nokeys,legs_nokeys
```

误差数字在每次录像日志的 `skirt against the game's keys` 一行。

## 6. Inase 完全用 Magica Cloth 2（10-05）

用户 10-05："inase完全用magic cloth 2 我看看效果吧"。

**新方案 `magica_full`**（F4 里有；装了 Magica 插件时，Inase 在游戏里默认就用它）：

- **全部用插件自带的预设**：每个部件导入 Magica 自己的预设参数（插件里的 `MC2_Preset_*.json`，用它的 `ImportJson`，只导参数、不动绑定）：
  - 三块长裙片（网格布 MeshCloth）和裙子：`MC2_Preset_Skirt`；
  - 头发：前发 `FrontHair`，后发和辫子按长度 `ShortHair` / `LongHair`；
  - 胸：Magica 专门做弹跳的 BoneSpring 模式 + `MiddleSpring`；
  - 链子、挂件：`Accessory`。
  - 只改一处：布料往"动画的姿势"恢复（animationPoseRatio 1）。预设里是 0，意思是往建布料时的姿势恢复；我们是在绑定姿势下建的，不改的话裙子会往 T 字姿势拉。
- **游戏动作里也由它来动**：技能、受击、出场的裙子关键帧不用，裙子照动捕动作那样处理；头发和胸的关键帧当作 Magica 的动画姿势，在上面模拟。
- **不用我们的"自然下垂"**（F5 那个），下垂交给 Magica 的重力。
- 还保留的是"动画姿势"那一层：裙子跟着腿走（`RoeSkirtRig`），这正是 Magica 文档建议的用法；碰撞体是我们量好的腿、胯、躯干胶囊。
- 预设文件不复制进仓库：建场景时把插件里的预设挂到战斗场景上（`RoeMagicaPresets`），打包时就带进 exe。
- Magica 只在游戏真正运行时动（编辑器里批量录像是手动一步步推的），所以只在运行时把 Inase 默认切到它；批量录像里仍是原来的方案。

**效果**：`out\a08_magica_full_front.mp4`（正面全身）、`out\a08_magica_full_backright.mp4`（背后胯部特写）。
动作是前进、后退、侧步、四个攻击，然后技能 1、技能 2，共 24 秒；左：Magica 全接管，右：原来的（骨骼布料，技能用游戏关键帧）。

- **动起来**：走路、侧步时长裙片往身后飘；回旋踢时两片长裙片大幅甩开，有点游戏手 K 的味道。原来的方案几乎一直垂着。
- **背后特写**：后片在屁股上折出柔软的褶子，踢腿时折叠着让开大腿。原来的后片像一块板，踢腿时腿直接穿过去（8.0、9.2 秒）。
- **技能里**：Magica 的裙子是它自己甩出来的，和游戏手 K 的不一样。比如技能 2（19.5 秒），游戏里裙片往前飞，Magica 的还垂着。
- **数字**（播放模式录像的测量，整段平均）：

| | Magica 全接管 | 原来的 |
|---|---|---|
| 布料顶点穿进身体超过 1 厘米 | 1.1% | 0.4% |
| 抖动（每帧位置的二阶差） | 8.7 毫米 | 4.8 毫米 |
| 拉伸 | 7.4% | 7.3% |

- 技能 1 里 Magica 有一处很大的跳动：最大抖动 33 厘米，最长的一条边拉长到 9 倍。技能开始时人一下子被动作带走，布料跟不上。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics      # 把 Magica 的预设挂到场景上
.\tools\unity_batch.ps1 -NoQuit -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run -Graphics -Extra '-roeChars','a08','-roeCloths','magica_full,magica_style','-roeScript','full','-roeView','front','-roeOut','E:\code\othercode\roe_fighter_unity\_work\a08_mc2full_front'
python tools\merge_takes.py _work\a08_mc2full_front_v _work\a08_mc2full_front:a08_magica_full=magica_full _work\a08_mc2full_front:a08_magica_style=old_default
python tools\cloth_demo_video.py _work\a08_mc2full_front_v out\a08_magica_full_front.mp4 --chars a08 --variants magica_full,old_default
```
