# ROE Fighter 2（Unity 版，横版）

用 Rise of Eros（ROE）、《死或生 6》、《Vindictus: Defying Fate》、《剑星》的角色做的**横版格斗游戏**，玩法照拳皇（The King of Fighters）：
两人站在一条线上面对面，镜头从侧面拍，三人组队轮战，能跳、能蹲、按住后退防御、搓招放技能，能量槽分三格。引擎是 Unity 6000.4.12f1 + URP。
2026-10-18 从 `roe_fighter_unity`（3D 版：能绕着对手转的格斗游戏，GitHub killinux/roe_fighter_unity，提交 03c0806）分出来：
角色、素材流程、着色器、物理、爆衣、技能系统、动作包全部继承；重写的是战斗本身（`Assets/RoeFighter/Runtime/Fight/`）。
玩法和按键见 [`docs/game-manual.md`](docs/game-manual.md)（exe 旁边有带截图的网页版 `out\ROEFighter2\游戏说明.html`）。

## 横版（拳皇式）版本（2026-10-18）

用户 10-18："切换到 roe_fighter_unity2，现有角色，把当前游戏改成类似拳皇的，横版对战的游戏"。

### 和 3D 版的区别

| 3D 版（roe_fighter_unity） | 横版（这里） |
|---|---|
| 两人在圆形场地里，能侧步绕着对手转；镜头站在两人连线的侧面，自己找不被遮挡的位置（`CameraRoom`） | 两人在一条线上（`FightGame.axis`），始终面对面，跳过对手头顶才换边；镜头固定在场地的一侧（`layout.normal`）正对这条线，跟着两人的中点左右移动、按两人距离拉近拉远（4.6–8.6 米）、有人跳起时抬高一点，画面不出场地边；两人最多隔 6.8 米（画面装得下） |
| W / S 侧步 | W 跳（点一下小跳、按住大跳；蹲一下再跳是超级跳 / 加速小跳；跑着跳更远），S 蹲；朝对手双击是跑，反方向双击是后撤步 |
| 按住后退防御，什么都能挡 | 站防挡中段和跳跃攻击，蹲防挡中段和下段；蹲着的脚（B、D）是下段，跳跃攻击是上段；投技挡不住 |
| 站立 A B C D（每人自己的） | 还是每人自己的；另加全角色共用的蹲 A B C D（扫腿击倒）、空中 A B C D、C+D 吹飞（打飞倒地）、贴身前 / 后 + C 投技、A+B 前滚回避（滚的过程中打不到、能穿过对手） |
| 受击、倒地（游戏自己的 clip） | 加上：蹲着挨打、下段受击、被打到空中（空中最多再追打两下，然后落地倒下）、吹飞、落地、起身；KO 在空中也先落地 |
| 一对一三局两胜 | 三人组队轮战（输的一方换下一个，赢的留场、恢复一点体力），`-roeTeam 1` 或 F8 切回单挑 |
| 能量槽满 100 放超必杀 | 三格（最多 300），超必杀用一格；投技涨 8 |
| 原地放的技能打到游戏里敌人站的那一排那么远（`HitsAhead`） | ROE 里本来就是远程的技能（INASE 技能 2 甩飞剑、KART 红衣的双枪技能 1、2、AMANO 的超必杀；`-roeRanged a08=skill2,b10=skill1+skill2,m02=skill3`，`FighterRig.rangedSkills`）全场任何距离都打得到，对手跳在空中（0.5 米以上）时打空；电脑离得远时会放（用户 10-18 "inase有远程攻击也加上"） |
| 爆衣：超必杀最后一下打中、K.O. | 再加 C+D 吹飞打中、投技摔出去各掉一段（用户 10-18 "爆衣效果也加上"） |
| 身体碰撞：竖直胶囊 0.15–1.6 米 | 蹲着时胶囊只到 0.95 米：站着打头的拳从蹲着的人头上掠过 |

### 代码

- `Runtime/Fight/Fighter.cs`：横版状态机（Idle / Walk / Run / BackDash / Crouch / Jump / Land / Attack / Block / Hurt / Launched / Down / Getup / Special / Roll / Throw / Thrown / KO / Win / Lose）。
  位置还是世界坐标 `pos`，沿线的位置 `U` 和高度 `H` 是它的两个投影；`facing` 是朝向（沿 axis 为 +1）。
  跳：起跳蹲 4 帧，离地时 W 还按着就是大跳（向上 6.6 m/s），松了就是小跳（4.4）；重力 15 m/s²；前后跳 2.6 m/s，超级跳 7.7 / 3.4。双击要在 0.25 秒内。
  空中攻击的判定、投技的抓取时刻、挡得住挡不住（`Blocks`）都在这里。
- `Runtime/Fight/FightGame.cs`：比赛（组队 / 单挑）、输入换算（朝对手 = 前）、两人不重叠、命中判定（对手在角落时推开攻击方）、投技的两下、技能命中、特效、回合规则、侧视镜头、选人（组队按顺序选三个）。
- `Runtime/Fight/FightAI.cs`：电脑对手，带小计划队列（跑过去、跳进来打）；`PlayerInput`：按键。
- `Runtime/Fight/FightHud.cs`：HUD 加了两队头像（被打倒的变暗）、三格能量；选人界面列出两队。
- `Runtime/Fight/MotionPack.cs`：角色（role）从 4 个扩到 24 个（蹲、起跳、跳、落、着地、站防 / 蹲防及其受击、蹲着受击、下段受击、空中受击、扫倒、击飞、落地、起身、前冲、后撤、投技双方）。
- `Runtime/Fight/FighterRig.cs`：`AddMotions`（基础包补上角色没有的 clip）、`Airborne`（在空中时不把脚按到地上，髋保持站立高度，由格斗逻辑抬起整个人）。
- `Editor/RoeMotionPacks.cs`：JSON 里 `roles` 按名字对应全部角色；招式多了 `stance`（stand / crouch / jump）、`kind`（blowback / throw）、`launch`。
- `Editor/RoeFightScene.cs`：建场景时导入基础包 `kof_basics`、设镜头那一侧 `normal`、`-roeTeam`、`-roeP1 a,b,c`；录像、打包改名 `ROEFighter2`；原来量镜头空间的 `CameraRoom` 和镜头统计删了。

### 基础动作包 `kof_basics`

`tools/motionpacks/kof_basics.json`：UFE 2 演示角色 Robot Kyle 的人形动画（蹲、起跳、跳、落、着地、站防 / 蹲防、蹲着受击、下段受击、空中受击、扫倒、击飞、落地、起身、
前冲、投技双方，蹲和空中的拳脚，挑空当 C+D），后撤步用 Mecanim Bot 的。每招的有效帧照 UFE 自己的招式表（`_work\ufe2\ufe_moves.tsv`，第 f 帧 = 片段的 f × 速度 / 60 秒）。
所有角色共用；她自己有的 clip（站架、走、跑、起身）优先。

### 怎么做

```powershell
cd E:\code\othercode\roe_fighter_unity2
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeCompileCheck.Run                      # 只看编译
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics             # 建场景（导入 kof_basics、渲染头像）→ Assets\RoeFighter\Scenes\Fight_e23_steel_s02.unity
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','a08,g04,raven','-roeP2','eve_nikke06,kas,fio005','-roeSeconds','300','-roeOut','_work\fight_kof'
python tools\make_video.py _work\fight_kof out\fight_kof_team.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.SelectDemo -Graphics -Extra '-roeOut','_work\select_kof'   # 选人界面录像（两队各选三个）
python tools\make_video.py _work\select_kof out\select_kof.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics       # → out\ROEFighter2\ROEFighter2.exe（约 2.8 GB）
.\tools\smoke_player.ps1 -Seconds 80 -Out _work\smoke                                           # 冒烟测试：启动 exe 直接开打（F1 电脑对电脑），每 10 秒截图，看 Player.log 有没有异常
python tools\make_manual.py --shot fight=_work\fight_kof\frames\01200.jpg --shot select=_work\select_kof\frames\00300.jpg   # → out\ROEFighter2\游戏说明.html
```

每次改了 C# 只要重新编译（或直接 BuildPlayer）；改了动作包的 JSON 要先 `RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/kof_basics.json'` 再重建场景（导入会重做 .anim 资源，场景里的引用会失效）；
改了 `FightGame` 字段的默认值（镜头、场地边距这些是存在场景里的）也要重建场景。

### 已知问题 / 待定

- 蹲、跳、防御、受击等共用动作是 Robot Kyle 的，套在这些女性身体上姿势偏硬；每个角色自己游戏里若有对应动作可以逐个替换（`tools/<game>/<id>.json` 的 `game` 表加同名 clip 即可覆盖）。
- 回避（A+B）用的是 Kyle 的前冲，不是真正的翻滚。
- 投技固定是"抓到身前再摔出去"一种，没有背摔；被抓的人在摔之前一直停在身前 0.85 米处。
- 没有 MAX 模式、保险、超级取消。
- 远程技能的判定是"全线命中"，但技能自带的光线 / 子弹特效还是游戏里的长度（约 3 米），只有命中特效出现在对手身上；没有做真正飞过去的弹道物体（可以做：把飞剑 / 子弹做成沿线飞行的对象，打中才算）。
- 组队赛里留在场上的人恢复 8–20% 体力（照剩余时间），数值随手定的。

## 原 3D 版本的说明（继承自 roe_fighter_unity，截至 2026-10-18；下面的命令里 `roe_fighter_unity` 在这个仓库里就是 `roe_fighter_unity2`）

用 Rise of Eros（ROE）的角色做的 3D 格斗游戏。引擎是 Unity 6000.4.12f1 + URP。原计划用 UFE 2（Universal Fighting Engine 2）做对战框架，暂时买不了，所以先自己写了一套能玩的格斗逻辑（见"格斗"一节）；角色、动作、技能、特效这些数据以后换 UFE 也照样能用。
角色：a08（Inase）、g04（Luf）首发，10-03 晚加了 b10（Kart）和 g05（Luf 的女神套装），10-04 加了《死或生 6》（DOA6）的霞（Kasumi），
晚上又加了霞的海盗裙（kas011，裙子袖子是 DOA6 的网格布），再加了《Vindictus: Defying Fate》的 Fiona（白羽礼服 PCF_005，`fio005`）；开局先进选人界面。

从游戏里取出的素材和买来的插件都不进这个仓库（见 `.gitignore`）：`Assets/ROE`、`Assets/DOA`、`Assets/VDF`、`Assets/RoeFighter/Generated`、`Assets/UFE*`、`Assets/MagicaCloth2`、`_work`、`out`。
这些素材来自商业游戏（ROE、DOA6、DOA5LR、Vindictus）和付费插件（UFE 2、Magica Cloth 2），只在本机自己用。

## 现状（2026-10-05）

| 内容 | 状态 |
|---|---|
| a08、g04 的高模、游戏材质、动作、音效进 Unity | 完成 |
| 五个角色着色器（服装、皮肤、头发、眼睛、眉毛睫毛） | 完成，算法照游戏编译后的着色器反汇编逐条重写（10-02） |
| 展示视频（两人各播一遍自带的待机、三个技能、受击，带技能音效） | `out\a08_g04_showcase.mp4` |
| Unity 人形骨架 + 自带动作转成人形动作 | 完成，和原动作的平均误差 0.03–1.1 厘米 |
| 互相套用动作（g04 播 a08 的技能等） | 能播；头发裙子没有物理，辅助骨骼还没有驱动 |
| 游戏自己的战斗场景（s01，城堡屋顶） | 完成，烘焙光照原样可用；自动找一条没有遮挡的对战线 |
| 技能表：每个技能的命中时刻、伤害分段、冲刺位移、震屏、特效、音效、语音 | 完成，从游戏的时间轴和技能演出设置里读出来 |
| 场景里的对打演示（按技能表演，带特效、音效、语音，最后一招击倒） | `out\a08_g04_duel_s01.mp4` |
| 技能特效（游戏原本的粒子预制体 + 六个重写的特效着色器 + 游戏原本的时间轴） | 完成：动作和特效都由游戏自己的时间轴驱动，打在对手身上的命中特效按技能表生成 |
| 挑开阔的战斗场景：194 个场景全部粗筛，5 个导入 Unity | 完成；默认用格斗俱乐部擂台 `e23_steel_s02` |
| 基础动作（走、退、跑、刺拳、直拳、踢、挥砍、防御架势）：公开动作捕捉数据转人形 | 完成；侧步改成程序化迈步（两节骨 IK，脚不滑） |
| 手臂和腿：皮肤挂在辅助骨上，从游戏动作里拟合出辅助骨怎么跟随肢体，动捕动作时实时驱动；动捕着地；走路不滑；高跟鞋站稳（鞋跟和前掌都着地） | 完成（10-02 下午，高跟鞋 10-02 晚），见"手臂和腿"一节；对比图 `out\feet_fix_1002.jpg` |
| 布料：裙子、头发、链子、胸的骨骼布料（移植自 bone_cloth 插件，照 Magica Cloth 2） | 完成，基础动作时生效；对比视频 `out\bone_cloth_demo.mp4`；10-02 晚裙子按游戏站姿垂下（之前翘出 45°），对比图 `out\skirt_fix_1002.jpg`。调研 Magica Cloth 2（`docs/magica-cloth-2.md`）后照它的做法重做：裙子跟腿走、一片布连成网，F4 切换新版 / 旧版 / 关，以后可接 Magica；对比视频 `out\skirt_magica_style.mp4`；之后又修了屁股附近后片翘起（裙子的基准改成游戏站姿），对比视频 `out\skirt_rest_compare.mp4`；10-03 裙子改成自然下垂、贴着身体（F5 可切回），对比视频 `out\skirt_drape_compare.mp4` |
| 动作包：基础动作可以整套替换（F3 切换，买来的人形动作写个 JSON 就能导入） | 两个包：`bandai1`（默认）和开源动捕做的 `accad_male2`（拳击架势、步法、前后腿回旋踢，10-02 晚）；对比视频 `out\motion_packs_demo.mp4`，用新包的整场对打 `out\fight_cpu_match_accad.mp4` |
| 能玩的格斗：移动、侧步、四个攻击键、防御、受击、倒地、三个技能（含超必杀）、能量槽、回合、计时、HUD、电脑对手、场地自带的战斗音乐 | 能玩：`out\ROEFighter\ROEFighter.exe`（10-05 重新打包：含 Fiona 自己的动作、剑和盾、新镜头），或在编辑器里打开场景按 Play；电脑对电脑的录像 `out\fight_cpu_match_1002b.mp4` |
| g04 的普通攻击换成不知火舞（DOA6）的四招；扇子缩到 0.7 倍 | 完成（10-03），见"不知火舞的普通攻击"一节；对比视频 `out\g04_mai_strikes.mp4`，扇子 `out\weapon_size\g04_fan_sizes.png` |
| UFE 2（用户 10-04 下载的 Source v2.7.0a） | 框架没换，格斗逻辑还是自己的；它演示角色的动作做成三个动作包 `ufe_kyle`、`ufe_ethan`、`ufe_bot`（F3），Fiona 的受击、倒地、起身、开场、胜利、三个技能也用它的。见"动作包 > 第三组：UFE 2 的演示角色"；对比视频 `out\ufe_packs_demo.mp4`。10-05：全部 20 个站立普通攻击放到 a08 身上给你挑，逐招视频 `out\a08_ufe_normals.mp4`，检查图 `out\a08_ufe_moves_sheet.jpg`；出招判定时间改用 UFE 自己的有效帧 |
| b10（Kart）、g05（Luf 女神）加入战斗：模型、动作、技能和特效、音效，都能爆衣到全身 | 完成（10-03 晚），见"新角色：b10 和 g05"一节；爆衣演示 `out\clothes_burst_demo_b10_g05.mp4`，电脑对电脑整场 `out\fight_cpu_match_b10_g05.mp4`，检查图 `out\clothes_burst\unity\b10_sheet.png`、`g05_sheet.png`。之后（10-03 晚第二轮）：g05 受击、倒地时落到地上，放技能时慢慢升空，飘带在游戏动作里也交给布料，对比 `out\g05_hover_fix.mp4`；a08、g04 的动作按新的肢体重算重转了。10-04 第三轮：g05 的脚和游戏对齐（后脚不再朝后站），对比图 `out\g05_feet_1004.jpg` |
| 选人界面：四个角色的头像卡片，选中的两人站在场上；两边选同一个角色时复制一份 | 完成（10-03 晚），见"选人界面"一节；演示 `out\select_screen_demo.mp4`。10-06 晚：选人时播 ROE 菜单里的展示动画——转向镜头循环 `idle_02`，确认时播 `react_01` 加她的台词 |
| Luffee（g04、g05）的站架也换成不知火舞的（DOA6 00000） | 完成（10-03 晚），对比视频 `out\luffee_mai_stance.mp4`（左：动作包的拳击架势；右：不知火舞的架势和四招） |
| 爆衣（比赛中把衣服打掉） | 完成到第二步（10-03）：超必杀的最后一下打中、被 KO，各掉一段（每人四段），决胜的 KO 把剩下的全打掉，整场比赛不回来，F6 开关；衣服下面换成家族的裸体底模，所以能掉到全身（g04 留着鞋），见"爆衣"一节；演示 `out\clothes_burst_demo.mp4`，检查图 `out\clothes_burst\unity\a08_sheet.png`、`g04_sheet.png`，电脑对电脑整场 `out\fight_cpu_match_burst.mp4`。调研和后面几步（补身体、打哪破哪）见 [`docs/clothes-burst.md`](docs/clothes-burst.md) |
| DOA6 的霞加入战斗；调研 DOA6、DOA5LR 的胸、头发、衣服物理；物理做成可插拔（F4 换方案，以后可接 Magica Cloth 2） | 完成（10-04），见"霞（DOA6）和可插拔物理"一节和 [`docs/doa-physics.md`](docs/doa-physics.md)；对比视频 `out\kas_physics_body.mp4`、`out\kas_physics_chest.mp4`（左：骨骼布料，中：DOA6 的物理，右：关），电脑对电脑 `out\kas_fight_test.mp4` |
| 霞的海盗裙（DOA6 `COS_011`，角色 `kas011`）：裙子、袖子、胸前片是 DOA6 的网格布，看得见的布每帧照游戏的公式从控制点重建 | 完成（10-04 晚），见 [`docs/doa-physics.md`](docs/doa-physics.md) 第 10 节；静止时重建和导入的模型逐顶点一致（≤ 0.01 毫米、法线 ≤ 0.2°）；对比视频 `out\kas011_grid_cloth_front.mp4`、`out\kas011_grid_cloth_back.mp4`（左：骨骼布料，中：DOA6 的网格布，右：关）。顺带：软体格子的静止形状改成跟着目标，胸站着更贴、甩完回得更快 |
| DOA5LR 式弹簧网（F4 的 `doa5lr_style`）：同样的骨链，约束换成 DOA5LR 网格布的横向、斜拉、隔一个、远程弹簧；霞的网格布在骨骼方案里也连成一片 | 完成（10-04 晚），见 [`docs/doa-physics.md`](docs/doa-physics.md) 第 11 节；对比视频 `out\doa5lr_style_compare.mp4`（g04、a08、霞的海盗裙；左：骨骼布料，中：DOA5LR 式，右：关）。比骨骼布料更贴动画、晃得少 |
| Magica Cloth 2 插件（用户 10-04 下载）：F4 的 `magica`；a08 的长裙片、g04 的裙片用 MeshCloth 直接模拟网格，其余骨链用 BoneCloth，霞的网格布模拟控制点 | 完成（10-04 晚），见 [`docs/magica-cloth-2.md`](docs/magica-cloth-2.md) 第 10 节；插件不进仓库，只在播放模式里跑（另写了播放模式录像）。对比视频 `out\mc2_compare_v2_front.mp4`、`out\mc2_compare_v2_back.mp4`（a08：左 Magica、中骨骼布料、右关；霞的海盗裙：左 Magica、中 DOA6 的、右关）。a08 的裙片有了褶皱、踢腿时搭在腿上；不是默认方案 |
| Vindictus 的 Fiona（白羽礼服 PCF_005，`fio005`）加入战斗；衣服、头发、胸用她游戏自己的物理：游戏里是 KawaiiPhysics 插件，每条链的参数从游戏文件里读出来，算法照插件源码搬过来（F4 的 `kawaii`，`auto` 下她默认用它） | 完成（10-04 晚），见 [`docs/vindictus-fiona.md`](docs/vindictus-fiona.md)；物理对比视频 `out\fio005_physics_front.mp4`、`out\fio005_physics_back.mp4`、`out\fio005_physics_chest.mp4`（左：她游戏的 KawaiiPhysics，中：我们的骨骼布料，右：关），电脑对电脑 `out\fight_cpu_match_fio005.mp4`，静帧 `out\fio005_stills.jpg`。鞋跟按鞋底量出来压脚（32°），平脚的动作套上来也踩在鞋跟上。10-04 夜：**动作换成她游戏里自己的**（长剑加盾，87 段转成人形动作），剑和盾也从游戏里拿来，攻击按剑身判定；MetaHuman 多出的脊柱节和扭转骨运行时补回（`RoeUeRig`）；检查图 `out\fio005_own_moves_sheet.jpg`，电脑对电脑 `out\fight_cpu_match_fio005_own.mp4`，前臂扭转对比 `out\fio005_twist.jpg`。10-05：剑和盾跟着游戏的动作转（举盾反击时盾朝前，胜利时反手握剑），前后对比 `out\fio005_props.jpg`；扭转骨、肩肘髋膝的修正骨、手指半关节照游戏自己的程序化骨骼驱动（从包里的 ControlRig 字节码解出来的），对比 `out\fio005_rig.jpg` |
| 对战镜头不跑到场地外面：建场景时量出镜头能站的地方，镜头只站在空处、看两人的视线不被挡（F8 切回旧镜头） | 完成（10-05），见 [`docs/fight-camera.md`](docs/fight-camera.md)；新旧镜头并排 `out\fight_camera_compare.mp4`，同一场电脑对电脑重录 `out\fight_cpu_match_fio005_cam.mp4`，地图 `_work\camera_room\e23_steel_s02.png` |
| 剑星（Stellar Blade）的 Eve（7 代潜降服，`eve09`）加入战斗；物理全部用剑星自己的：胸、臀、大腿、护腕是 UE4 的 SpringBone，前发、鬓发、马尾根、领带是 KawaiiPhysics，马尾后段和半透明披风是 PhysX 刚体链，参数都从游戏包里读出来（F4 的 `stellar`，`auto` 下她默认用它；没用 Magica Cloth 2） | 完成（10-05），见 [`docs/stellar-blade-eve.md`](docs/stellar-blade-eve.md)；物理对比视频 `out\eve09_physics_front.mp4`、`out\eve09_physics_back.mp4`（左：剑星自己的，中：我们的骨骼布料，右：关），电脑对电脑 `out\fight_cpu_match_eve09.mp4`（对 a08）。动作暂时是动作包加 UFE 2 的受击和技能；她游戏里自己的动作、手肘膝盖的修正骨还没做 |
| DOA6 的不知火舞（`mai`，默认红衣 `MAI_COS_004`）加入战斗；物理用 DOA6 自己的：胸和臀是软体，衣服的 5 块垂布和马尾是网格布，身后的长穗和肩上的绳是骨链 | 完成（10-05），见下面"不知火舞（DOA6）"一节和 [`docs/doa-physics.md`](docs/doa-physics.md) 第 12 节；物理对比视频 `out\mai_physics_front.mp4`、`out\mai_physics_back.mp4`（左：DOA6 自己的，中：我们的骨骼布料，右：关），电脑对电脑 `out\fight_cpu_match_mai.mp4`（对霞）。站架和四个攻击就是 g04 用的那套 |
| Eve 的百褶裙（Office Style，`eve37`）：裙子除了剑星的 KawaiiPhysics，还跑游戏自己的裙子 Control Rig——直接执行它的 UE 4.26 RigVM 字节码，按大腿抬起多少把各裙片转开 | 完成（10-05），见下面"Eve 的百褶裙（eve37）和裙子的 Control Rig"一节和 [`docs/stellar-blade-eve.md`](docs/stellar-blade-eve.md) 第 6 节；对比视频 `out\eve37_skirt_hips.mp4`（胯部特写）、`out\eve37_skirt_back.mp4`（左：剑星自己的，中：不跑 Control Rig，右：我们的骨骼布料），踢腿放大 `out\eve37_skirt_kick.jpg` |
| Inase 的裙子调研：游戏里逐帧手 K、没有物理；拿她自己的技能当标准答案，我们的骨骼布料、Magica Cloth 2、只跟着腿都差 61–63°，游戏里长裙片大幅飘开，三种方案都贴着腿垂 | 调研完成（10-05），见下面"Inase 的裙子：游戏里怎么动的"一节和 [`docs/inase-skirt.md`](docs/inase-skirt.md)；对比视频 `out\a08_skirt_vs_keys.mp4`；改法等你定 |
| ROE 战斗里有没有物理：战斗角色、敌人、技能都没有物理组件，舞台只有不会动的碰撞体和少量会碰撞的粒子；真正的物理（Magica Cloth 2）只在大厅 | 调研完成（10-05），见下面"ROE 战斗里有没有物理"一节和 [`docs/roe-battle-physics.md`](docs/roe-battle-physics.md)；工具 `tools/roe_physics_scan.py` |
| Inase 完全用 Magica Cloth 2（`magica_full`）：所有部件用插件自带的预设，技能里也由它来动；装了插件时她在游戏里默认就用它（F4 可切换） | 完成（10-05），见 [`docs/inase-skirt.md`](docs/inase-skirt.md) 第 6 节；对比视频 `out\a08_magica_full_front.mp4`、`out\a08_magica_full_backright.mp4` |
| 身体检查：10 个角色的爆衣、乳摇、身体权重，Inase 的头发和衣服（`RoeBodyCheck`、`RoeClothPlayDemo` 的胸 / 头发视角和读数）；修了 Inase 的胸（Magica 弹簧太软，胸甲滑开） | 完成（10-05），见 [`docs/body-check.md`](docs/body-check.md)；视频 `out\body_check_roe_jiggle.mp4`、`out\body_check_others_jiggle.mp4`、`out\a08_breast_springs.mp4`、`out\a08_hair_check.mp4` |
| Eve 两套（eve09、eve37）也能爆衣：剑星的裸体 mod 当衣服下面的身体，被衣服盖住的地方等那件掉了再画 | 完成（10-05），见"Eve 也能爆衣"一节；检查图 `out\clothes_burst\unity\eve09_sheet.png`、`eve37_sheet.png`，演示 `out\clothes_burst_demo_eve.mp4` |
| Fiona（fio005）也能爆衣：衣服下面用游戏自己的素体（以前没导出过），在 Blender 里贴到 PCF_005 的皮肤上；四段掉到全身，高跟鞋留着 | 完成（10-06），见"Fiona 也能爆衣"一节；检查图 `out\clothes_burst\unity\fio005_sheet.png`，演示（对 Inase）`out\clothes_burst_demo_fio005.mp4` |
| Inase 拿巨剑打普通攻击：ROE 和 UFE 2 都没有现成的拿剑普通攻击，从 Vindictus 的剑招和她自己的技能 1 里挑了四招；剑一直握在右手（握法从她游戏自己的技能动作里量出来），按剑身判定 | 完成（10-06），见"Inase 拿巨剑的普通攻击"一节；13 招候选 `out\a08_sword_candidates.mp4`、`out\a08_sword_candidates_sheet.jpg`，定下的四招 `out\a08_sword_strikes.mp4`、`out\a08_sword_moves_sheet.jpg`、`out\a08_sword_check_sheet.jpg`，电脑对电脑 `out\fight_cpu_match_a08_sword.mp4`（对 Fiona）；换招改 `tools/motionpacks/a08_sword.json` |
| 卡地亚（Kart，b10，红色短夹克）拿斧头打普通攻击：她 10-03 就在战斗里，但以前用拳脚；现在照她游戏的站架，小斧头握在右手、两把枪插在大腿枪套里（枪套被爆衣打掉时枪一起消失），四个普通攻击用斧头；枪还是在技能里自己飞出来开火，和游戏一样 | 完成（10-06），见"卡地亚（Kart，b10）拿斧头打普通攻击"一节；14 招候选 `out\b10_axe_candidates.mp4`，定下的四招 `out\b10_axe_strikes.mp4`、`out\b10_axe_check_sheet.jpg`，电脑对电脑 `out\fight_cpu_match_b10_axe.mp4`（对 Inase）；换招改 `tools/motionpacks/b10_axe.json`。大腿根（10-06 晚）：屁股坠到大腿上是这几招把着地的腿往里转了 45–60°（游戏自己最多 36°），限制住了，对比 `out\b10_thigh_root.jpg`、`out\b10_axe_strikes_compare.mp4` |
| 两个 Luf 的裙子和飘带：四种物理并排比（我们的骨骼布料、Magica Cloth 2 用我们的参数、Magica Cloth 2 用插件预设、关），出招和选人展示动作都录了 | 完成（10-06 晚），见"Luf 的裙子：四种物理对比"一节；结论是两人都留在骨骼布料（不改默认）。对比视频 `out\luf_skirt_front.mp4`、`out\luf_skirt_back.mp4`、`out\luf_showcase.mp4`，静帧 `out\luf_skirt_compare.jpg` |
| Fiona 的肘部检查：她全部 108 段动作里肘弯多深、有没有反弯，最深的时刻拍肘部特写（修正骨不驱动 / 游戏规则） | 完成（10-06 晚），见 [`docs/vindictus-fiona.md`](docs/vindictus-fiona.md)"肘部检查"；没看出毛病。特写 `out\fio005_elbow.jpg`、`out\fio005_elbow_left.jpg`（藏起盾看左肘） |
| 天野（m02）加入战斗：模型、三个技能（游戏的 skill_01–03，带特效、音效、语音）、爆衣到全身；四个 ROE 角色原地放的技能按游戏的距离打得到（以前离开 2.9 米就打空） | 完成（10-11），见"天野（m02）加入战斗"一节；技能录像 `out\skills_reel.mp4`（m02、Inase、Luf、Luf 女神各放三个技能），爆衣 `out\clothes_burst_demo_m02.mp4` |
| 两个 Luf 的衣服和游戏里比：游戏怎么做的（战斗全是手 K，大厅只有头发用 Magica Cloth 2），手 K 甩多大；在她们自己的技能里关掉关键帧，我们的骨骼布料、Magica Cloth 2 两种设置和游戏手 K 并排比 | 完成（10-11），见"Luf 的衣服：和游戏里比"一节；视频 `out\luf_cloth_vs_game.mp4` |
| Luf 的裙子在动捕动作里也甩起来：裙子的物理参数照游戏手 K 自动调（`RoeSkirtTune`），新方案"swing"，Luf 默认用它 | 完成（10-11），见"Luf 的裙子照游戏甩"一节；新旧对比 `out\g04_skirt_swing_front.mp4`、`out\g04_skirt_swing_backright.mp4`；exe 已重新打包 |
| Luf 的裙子粘腿：两个原因——"swing"下垂太少，前片跟着大腿走；她垫在衣服下面的裸体有 6 个大腿顶点挂在裙子骨上。都修了 | 完成（10-11），见"裙子粘腿"一节；踢腿特写在 `out\g04_skirt_swing_backright.mp4`；exe 已重新打包 |
| Luf 左边的裙子翘起来：游戏的战斗站姿里左裙片下摆本来就往外翘 30° 左右；我们的蹲低架势里长裙片碰到地面，"swing"的重力太轻时窄裙片从地面往外卷。重力改回 5 | 完成（10-11），见"左边的裙子翘起来"一节；对比图 `out\g04_left_panel.jpg`；exe 已重新打包 |
| Luf 胯部那一段和游戏一样：每片裙子最上面两根骨用照游戏站姿拟合的形状，不再往下拉、不再被推出皮肤；从第三根骨往下照旧下垂 | 完成（10-11），见"胯部那一段和游戏一样"一节；对比图 `out\g04_hip_segment.jpg`；exe 已重新打包 |
| Luf 站游戏里的战斗站姿（不再是不知火舞的蹲姿）；数字键 1、2、3 放技能 1、技能 2、超必杀（ROE 角色的就是她游戏里的三个战斗技能） | 完成（10-11），见"Luf 站游戏里的姿势；技能键 1、2、3"一节；对比视频 `out\g04_roe_stance.mp4`，对比图 `out\g04_roe_stance.jpg`；exe 已重新打包 |
| Luf 的裙子重新从 ROE 导出核对（原始数据和游戏动作都没改乱）；照 ROE 自己的物理做了方案 `roe`；默认改成站在游戏站姿里时直接放 ROE 的手 K，动起来再交给布料 | 完成（10-11），见"Luf 的裙子：重新从 ROE 导出，完全照 ROE 再看"一节；视频 `out\g04_skirt_roe.mp4`，图 `out\g04_skirt_roe.jpg`；exe 已重新打包 |
| 游戏说明：操作、规则、技能、爆衣、功能键、14 个角色的招式、物理方案、启动参数、常见问题 | 完成（10-11），`docs\game-manual.md`；带截图的网页 `out\ROEFighter\游戏说明.html`（`tools\make_manual.py` 生成） |
| Kart 最新的一套（b14，白蓝鹤袍，游戏里 2026-09-12 上的）加入战斗：模型、三个技能（游戏的 skill_01–03，带特效、音效、语音）、选人时的展示动作和台词、爆衣四段到全身；右边拖到膝下的长纱袖交给布料，抬手时垂下来 | 完成（10-11），见"Kart 最新的一套（b14，鹤袍）加入战斗"一节；技能 `out\b14_skills.mp4`，爆衣 `out\clothes_burst_demo_b14.mp4`，电脑对电脑（对 b10）`out\fight_cpu_match_b14.mp4`，检查图 `out\clothes_burst\unity\b14_sheet.png`；exe 已重新打包 |
| Crane Kart（b14）拿剑打普通攻击：剑一直握在右手（她技能里挥剑时的正手握），四招里三招截自她自己的技能、一招是 Vindictus 的翻身斩；全身那段右肩顶的棱修掉了（右肩照左肩镜像取权重） | 完成（10-17），见"Crane Kart（b14）拿剑打普通攻击；右肩照左肩取权重"一节；四招 `out\b14_sword_strikes.mp4`，候选 `out\b14_sword_candidates.mp4`，电脑对电脑（对 Inase）`out\fight_cpu_match_b14_sword.mp4`，右肩对比 `out\b14_shoulder_fix.jpg`，爆衣演示重录；exe 已重新打包 |
| Kart 鹤袍的粉白换色款（b14 的 outfit1）做成新角色 PINK CRANE KART：材质、贴图、白发粉梢、粉色技能特效都是游戏里换色款自己的，网格、动作、爆衣规则、握剑、剑招沿用 b14 | 完成（10-17），见"Kart 鹤袍的粉白换色款（b14_outfit1）加入战斗"一节；技能 `out\b14_outfit1_skills.mp4`，剑招 `out\b14_outfit1_sword_strikes.mp4`，爆衣 `out\clothes_burst_demo_b14_outfit1.mp4`，粉白对蓝白 `out\fight_cpu_match_b14_outfit1.mp4`；exe 已重新打包 |
| Inase 两招之间站她在 ROE 里的战斗站姿（剑尖拄地、手扶剑柄），出招时剑才转进挥砍的握法 | 完成（10-17），见"Inase 站游戏里的姿势"一节；对比视频 `out\a08_roe_stance.mp4`，对比图 `out\a08_roe_stance.jpg`；exe 已重新打包 |
| 剑星 Eve 的粉色紧身衣（NIKKE 联动的 Cooling Suit，`eve_nikke06`）加入战斗：模型、贴图、物理都照游戏读，四段爆衣到全身 | 完成（10-17），见"剑星 Eve 的粉色紧身衣"一节；爆衣 `out\clothes_burst_demo_eve_nikke06.mp4`，电脑对电脑（对 eve09）`out\fight_cpu_match_eve_nikke06.mp4`；exe 已重新打包 |
| Eve 粉色紧身衣换上 Alice 的发型（粉色双马尾、猫耳耳机，游戏自己的物理），用她自己剑星里的招式：四个普通攻击、三个技能、受击倒地起身开场胜利，手里拿着血刃 | 完成（10-17），见"Eve 粉色紧身衣：Alice 的发型、她自己剑星里的招式"一节；四招 `out\eve_nikke06_sword_strikes.mp4`，候选 `out\eve_nikke06_sword_candidates.mp4`，电脑对电脑 `out\fight_cpu_match_eve_nikke06_own.mp4`，发型 `out\eve_nikke06_alice_hair.jpg`；exe 已重新打包 |
| eve09、eve37 也换成她自己剑星里的招式（四招、三个技能、受击倒地起身开场胜利），手里拿血刃 | 完成（10-17），见"eve09、eve37 也用她自己剑星里的招式"一节；电脑对电脑 `out\fight_cpu_match_eve09_eve37_own.mp4`；exe 已重新打包 |
| 剑星的渡鸦（Raven）加入战斗：模型、贴图、她游戏里自己的物理（长发是 130 个刚体）、她的长剑、她自己的动作（四个普通攻击、三个技能、受击倒地起身开场胜利），四段爆衣到她战衣下面游戏自带的完整身体；全部角色的爆衣重新检查 | 完成（10-18），见"剑星的渡鸦（Raven）加入战斗"一节；四招 `out\raven_sword_strikes.mp4`，候选 `out\raven_sword_candidates_sheet.jpg`，电脑对电脑 `out\fight_cpu_match_raven.mp4`，爆衣 `out\clothes_burst_demo_raven.mp4`、`out\clothes_burst\unity\raven_sheet.png`；exe 已重新打包 |

## 目录

```
Assets/RoeFighter/Shaders/     角色、场景、特效着色器和公共代码
Assets/RoeFighter/Runtime/     运行时也要用的代码：技能表的数据结构，Fight/ 下是格斗逻辑
Assets/RoeFighter/Editor/      编辑器脚本：建工程设置、拼角色、拍图、转动作、场景、对打演示、各种检查
Assets/RoeFighter/Settings/    渲染管线、影棚的后期和地面
Assets/ROE/                    （不入库）从游戏取出的素材，按角色和游戏包分文件夹
Assets/DOA/                    （不入库）DOA6 角色的模型、贴图、物理数据、动作（Assets/DOA/<id>）
Assets/VDF/                    （不入库）Vindictus 角色（Fiona）的模型、贴图、物理数据（Assets/VDF/<id>）
Assets/SB/                     （不入库）剑星角色（Eve）的模型、贴图、物理数据（Assets/SB/<id>）
Assets/RoeFighter/Generated/   （不入库）拼好的角色预制体、人形骨架、转好的动作、mocap/ 下是动作捕捉转成的基础动作
Assets/RoeFighter/Scenes/      （不入库）生成的对战场景（游戏的战斗场景 + 两个角色 + 格斗逻辑）
tools/                         Python / PowerShell 工具
docs/                          调研记录（Magica Cloth 2、爆衣、DOA 的物理是怎么做的）
_work/                         （不入库）游戏包的副本、提取结果、日志、渲染帧
out/                           （不入库）给人看的视频和图
```

## 从零重做一遍

```powershell
cd E:\code\othercode\roe_fighter_unity

# 1. 把角色用到的游戏包拷到 _work\bundles（a08、g04 两个是 94 个、约 430 MB）
python tools\stage_bundles.py _work\bundles a08 g04 b10 g05

# 2. 另开一个窗口启动 AssetRipper 并留着它，然后回到这个窗口导出成 Unity 工程
#    （另一个窗口）E:\tools\AssetRipper_1.3.14\AssetRipper.GUI.Free.exe --headless --port 5599 --log-path _work\assetripper.log
python tools\rip.py          # 设置 → 读入（缺的依赖包自动补）→ 导出到 _work\ripped，约半分钟

# 3. 拷进工程，材质改指向这里的着色器，写出清单
python tools\import_ripped.py

# 3b. 爆衣用的家族裸体底模（a08 用 a01，g04 用 g01，b10 用 b01，g05 用 g01_fm）：单独还原，只挑身体、材质、贴图，不动角色清单（见"爆衣"一节）
#     （--log 必须是 AssetRipper 服务启动时给的那个日志文件，rip.py 靠它找缺的依赖包）
python tools\stage_bundles.py _work\bundles_nude --nude a01 g01 b01 g01_fm
python tools\rip.py --bundles _work\bundles_nude --out _work\ripped_nude --log _work\assetripper.log
python tools\import_nude.py a01 g01 b01 g01_fm

# 4. Unity 里的步骤（都是命令行，不用开编辑器）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeProjectSetup.Run -Graphics        # 渲染管线，只需一次
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeShaderCheck.Run -Graphics         # 编译全部着色器变体
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFighterBuilder.BuildAll           # 拼角色预制体
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeShowcase.Stills -Graphics         # 每人四张定妆图
python tools\make_sheet.py
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeShowcase.Video -Graphics          # 渲染视频帧
python tools\make_video.py                                                                  # 合成 mp4，配技能音效
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHumanoidClips.ConvertAll          # 人形骨架 + 转动作 + 误差报告
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeShowcase.RetargetStills -Graphics # 互相套用动作的检查图
```

`unity_batch.ps1` 跑完会把日志里带 `[ROE]` 的行和报错打印出来，完整日志在 `_work\logs\`。
`-Graphics` 表示要用显卡（渲染、编译着色器时需要）。同样的功能在编辑器菜单 **ROE Fighter** 下也有。

## 每一步是怎么做的

### 素材：直接取 Unity 原生资源

游戏是 Unity 2022.3 做的，所以不走 FBX，用 AssetRipper 把游戏包还原成 Unity 自己的资源：预制体、网格、材质、贴图、动作、音频都是原样，没有任何格式转换的损失。

- 一套服装涉及的游戏包（`stage_bundles.py` 里的规则）：
  - `chara_armor_pc_<id>_hd & ld_hd`：高模预制体、展示动作（idle_02、react_01/02）、武器。
  - `chara_armor_pc_<id>_hd & ld_ld*`：战斗用低模 + **战斗动作**（idle_01、skill_01..03、hurt、die、rip）。
  - `meta_armor_pc_<id>_hd & ld`：展示用的完整角色，**每个网格该用哪个材质**只有这里有，另外还带游戏自己的布料物理设置。
  - `chara_mat_*`、`chara_tex_*`：材质和贴图；头部、公共贴图在 `…_common_*` 包里。
  - `sfx_battlefield_pc_<id>*`、`voice_battlefield_<语言>_pc_<家族>01*`：技能音效和语音。
- 高模预制体上挂的是占位材质，`RoeFighterBuilder` 按网格名从 meta 预制体里把真材质抄过来。
- a08 的高模没有武器网格，战斗低模里有（顶点数和高模武器相同，绑在同名骨骼上），拼角色时接到高模骨架上。
- 包和包之间靠内部编号（CAB）引用。编号不是文件名的哈希，`cab_index.py` 读每个包的文件头建索引；`ar_load.py` 看 AssetRipper 的日志，缺哪个补哪个。
- 游戏的展示视频（`gacha_video_gacha_special_ssr_pc_<id>.ab`）是游戏自己渲染的画面，`tools\extract_video.py` 能取出来，调材质时当参照。

### 着色器

AssetRipper 还原不出游戏着色器的源码，只给参数表（名字、范围、开关）。这里的着色器**参数名和游戏的一样**，所以游戏的材质文件原样能用，`import_ripped.py` 只把材质里的着色器引用换成这里的。

源码虽然没有，**编译后的程序是有的**：`tools\shader_asm.py` 从 `heros_shader_collection.ab` 里取出每个着色器、每种开关组合的 DirectX 11 程序，用 Windows 自带的 `d3dcompiler_47.dll` 反汇编，并把常量缓冲区的布局（哪个偏移是哪个材质参数）、贴图槽位、混合和深度状态一起写成文本（`_work\shader_asm\`）。照着它就能把算法一行一行对出来。特效着色器（见"技能"一节）和五个角色着色器都是这样重写的。
`tools\mat_params.py a08 g04 [--shader ROE/Skin] [--props _BumpScale,...]` 列出角色实际用到的材质参数，用来判断哪些差别真的看得出来。

```powershell
python tools\shader_asm.py                                   # 列出游戏的全部着色器
python tools\shader_asm.py "Pinkcore/Particles"              # 名字里含这段的都导出
python tools\asm_variant.py _work\shader_asm\Pinkcore_Particles_UnlitMaster.txt f "DISSOLVE DISTORTION _COLORMODE_MULTIPLY _EMISSIONMODE_SELF"
```

| 着色器 | 对应游戏的 | 要点 |
|---|---|---|
| `ROE/Character` | ErosLit/Character | 就是 URP 标准的 PBR：主光 GGX、球谐环境光、反射探针（都乘 MGA 的遮蔽），点光聚光按顶点。另加：细节法线用 RNM 混合；掠射角的反射乘 `_FresnelStrength`；最终颜色在边缘按 `(1-N·V)^4 × _RimColor.a` 插值到 `_RimColor`（受击闪光用）。底色图用 `_BaseMap_ST`，法线、MGA、自发光用 `_UVScaleOffset`，细节法线用原始 UV × `_DetailNormalMap_ST` |
| `ROE/Skin` | Skin | 漫反射 = 游戏的皮肤 LUT（横轴：模糊法线的半兰伯特；纵轴：MGAC 的 A × `_LutCurveStrength`）×（底色 + 透光色）。模糊法线是法线图往模糊方向偏 `_BumpBias` 级 mip，不带细节和强度。"透光"其实是越靠轮廓越强的红色：`(0.04 + 0.96 (1-N·V)^_TranslucentPower) × _TranslucentColor × MGAC.a`，跟着 LUT 和主光走。高光是 GGX，但乘的是模糊法线的 N·L。环境反射不加掠射增强 |
| `ROE/Hair` | KajiyaKayHair | 漫反射 = 底色 × 0.954 × lerp(N·L, 1 − abs(发丝·L), 0.33)；两层 Kajiya-Kay 高光都乘 `0.5 × (1 − 0.954 × 底色)`（深色头发反而更亮）和 N·L。环境光是把视线方向当光源代进同一个公式再乘球谐，发丝遮蔽只作用在环境光上。游戏分"深度预写 + 不透明 + 半透明边缘"三遍画；这里是一遍按不透明裁切画（配合 MSAA 抗锯齿），能投影、能参与景深 |
| `ROE/Eye` | Eye | 在眼球 UV 圆盘里合成。虹膜范围是遮罩图的 R；虹膜贴图按 `lerp(_PupilMinSizeInv, _PupilMaxSizeInv, _PupilSizeScale) × _IrisLimbusScaleInv` 缩小采样（贴图里的小瞳孔就是这样放大的），再按遮罩图 G 通道的深度做视差；角膜缘是按枢轴半径算出的一圈，颜色直接替换。漫反射用眼球自身的法线；高光是 GGX，但在虹膜上把 N·H、L·H 重新拉伸成一个清晰的圆斑，并封顶 `_SpecularTermMax`；反射按 4% 起、到边缘升到掠射值，与金属度无关。遮蔽图是**白色 = 被眼睑挡住** |
| `ROE/Eyebrow` | Eyebrow | 底色 × 0.96 × (主光 N·L × 阴影 + 球谐 + 顶点点光)，没有高光；材质既预乘 alpha 又按 SrcAlpha 混合（颜色 × alpha²），所以眉毛睫毛的软边偏深、显得浓 |

和游戏不同的地方（都是有意的）：
- **瞳孔大小**：两个角色的眼睛材质里 `_PupilSizeScale` 是 0.56（a08）和 0.76（g04），按游戏的公式瞳孔会占可见虹膜的 45–60%；而游戏自己渲染的展示视频里，瞳孔正好是这个值取 0 时的大小，说明游戏运行时会改它。`RoeFighterBuilder` 拼角色时把它设为 0（日志里有一行）。
- 眼球网格的切线不可靠，眼睛着色器用屏幕空间导数自己算切线（游戏用网格切线）。
- 头发的画法见上表；场景里的额外方向光（影棚的补光、轮廓光）逐像素算，游戏里没有这种光。

踩过的坑：
- 眼球网格的切线不可靠，用它算视差会让整只眼睛变黑。眼睛着色器改用屏幕空间导数自己算切线。
- 虹膜贴图里的瞳孔只有虹膜的 12%：游戏是把虹膜贴图缩小采样（等于放大）得到正常大小的，见上表。
- 这个版本的 URP，`UnpackNormalScale` 是先由未缩放的 xy 算出 z、再缩放 xy（z 不变），和游戏头发着色器的做法一样；
  游戏的服装、皮肤着色器则是把整个法线向 (0,0,1) 插值（`RoeCore.hlsl` 的 `RoeUnpackNormalLerp`）。
- 游戏的眼睛遮蔽图是白色表示被挡住（`1 − 贴图 × 强度`），和常见的 AO 图正好相反。
- 换新材质后的第一帧会用占位着色器渲染（皮肤发紫、衣服不见）。拍图前先空渲两帧。
- Unity 的方向光强度 1 就是"白色表面输出 1"，主光给 2 以上皮肤直接过曝。

### 展示视频

`RoeShowcase.Video` 不进播放模式，逐帧把动作采样到角色上再渲染，所以结果每次都一样。
镜头先扫一遍整段动作里骨骼（含武器）的范围，再做带"预知"的平滑：起跳、突进之前镜头就开始动，人不会出画。
`make_video.py` 按 `timeline.json` 把每个技能对应的游戏音效叠上去。

### 人形骨架和动作转换（给 UFE 用）

UFE 自带的格斗动作是 Unity 的人形动作，要让 ROE 角色能用，角色得有人形骨架。游戏里这些角色是普通骨架（3ds Max Biped）。

- `RoeHumanoid.BuildFighter`：
  - 按 Biped 的骨骼名对应到 Unity 的 52 根人形骨骼；
  - 把四肢摆直成 T 姿势（原始姿势肘部弯 11–17 度），脚保持高跟姿势不动，这样平底的动作套上来脚还是踩着高跟；
  - 有的服装大腿挂在脊柱下、锁骨挂在脖子下（g04 就是），人形骨架骨长固定，这种挂法脊柱一弯髋关节就跟着跑。做法是把这四根骨骼改挂到骨盆和上胸（世界姿势不变，蒙皮不受影响）；
  - 肢体扭转全部放在下一级关节（`upperArmTwist` 等四项设 1）。这些骨架有自己的扭转辅助骨骼，用 Unity 默认的 0.5，辅助骨骼会偏 7–13 厘米；
  - 武器改挂到待机时离它最近的骨骼上（g04 的扇子挂右手；a08 的剑待机时插在地上，挂到了骨盆），其余非人形骨骼的默认值取待机第一帧（g04 的扇子在原始姿势是完全展开的巨大状态，待机里才收起缩小）。
  - 输出 `<id>_fighter.prefab` 和 `<id>_humanoid_avatar.asset`；原来的 `<id>.prefab` 不变。
- `RoeHumanoidClips.ConvertAll`：把游戏的普通动作转成人形动作。
  - 在原骨架上采样原动作，把姿势按骨骼名抄到 fighter 骨架上，用 `HumanPoseHandler` 读出肌肉值和根运动，每帧一个关键帧；
  - 头发、裙子、胸、扭转辅助骨骼、脸这些非人形骨骼保留原来的曲线；
  - 骨盆以上的节点（Root、Bip001）不再带曲线，挂在它们下面的东西和改挂过的武器按新父级重算曲线；
  - 转完自动和原动作逐帧比对所有蒙皮骨骼的位置，日志里每个动作一行。目前平均 0.03–1.1 厘米，最差 1–3 厘米（都在扭转辅助骨骼上）。

### 战斗场景（游戏自己的舞台）

游戏有 194 个战斗场景（`scene_battlefield_*.ab`），每个只有几十 KB，引用的模型、贴图、光照图在别的包里。

```powershell
# 取一个场景：只放场景包，依赖的包（这个场景是 107 个、76 MB）会自动补齐
mkdir _work\bundles_stage_s01
copy "<游戏>\RiseOfEros_Data\StreamingAssets\AssetBundles\scene_battlefield_level000_s01_day.ab" _work\bundles_stage_s01\
python tools\rip.py --bundles _work\bundles_stage_s01 --out _work\ripped_stage_s01
python tools\import_stage.py s01                                   # → Assets\ROE\stages\s01
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeStage.Report -Graphics   # 场景里有什么
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeStage.Stills -Graphics   # 两人站上去拍四张
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeDuel.Run -Graphics       # 轮流放技能的演示帧（要先有技能表，见下一节）
python tools\make_video.py _work\duel _work\duel\duel.mp4
python tools\scene_tree.py <场景.unity> [--scripts]                                # 不开 Unity 看场景层级和游戏脚本的数据
```

- 场景文件、烘焙光照图（2 张）、光照探针、雾、天空盒、反射贴图都能原样带过来，Unity 打开场景后光照图自动生效。
- 新写了两个着色器：`ROE/Environment`（和角色服装同一套表面模型，加上光照图）和 `ROE/Skybox`（立方体贴图 + 地平线雾）。
- `RoeStage.Open` 会去掉工程里没有的游戏脚本、关掉游戏自己的相机、隐藏没有替代着色器的东西（这个场景是 11 个粒子特效）。
- 场景里的 `BattleFieldSetting/FormationSetting` 是游戏的站位：`Center` 是中心，双方各在中心线两侧 3 米。
- **对战线怎么选**（`RoeStage.FindLayout`）：游戏是回合制，双方沿场景纵深面对面站，镜头从己方身后看过去；格斗游戏要从侧面看，
  照搬游戏的站位，镜头和人之间全是场景里的尖塔、箱子。做法是给场景网格临时加碰撞体，对游戏战斗中心附近的每个位置、每个朝向做射线检测：
  脚下 2.4 米宽的地面要平、上面不能有东西，镜头在几种距离上到对战线各点的视线都不能被挡。先按 12 米长的对战线找，找不到再缩短。
  s01 的结果是：就在游戏的战斗中心，镜头顺着红毯看向城门（正是游戏自己的视角方向），两人横在红毯上，对战线 7 米。
- **朝向**：游戏的动作是按"模型根节点正对敌人"做的，站姿本身侧身（胯部转 20–40 度）。按脚尖方向摆会让上身和脸偏开对手。
  侧身只在屏幕一侧是正面朝镜头，另一侧的角色整体左右镜像（格斗游戏的常规做法），`RoeStage.Place` 自动判断。
- `RoeDuel.Run` 出片前会沿实际镜头轨迹再做一遍视线检查，日志里报告有多少帧被场景挡住。这段演示不是对战，没有判定和操作。

### 技能：按游戏自己的数据演

游戏里每个单位（一套服装）有一个玩法预制体 `gameplay_prefab_<单位>.ab`（a08 是 `suit_ines_8`，g04 是 `suit_luf_4`），
一个技能怎么演全在里面：

- 每个动作（待机、三个技能、受击、倒地……）是一条 Unity **时间轴**：哪一秒播哪个动作片段、哪一秒打开哪个特效、哪一秒放哪个音效和语音。
- 时间轴之外的由 `CharacterSkillView` 组件规定：**伤害数字出现的时刻和每段的比例**（a08 的 1 技能是 0.85 / 2.7 / 2.9 秒三段）、
  角色什么时候冲到对手面前（离对手 2 米 = 自己的攻击半径 + 对手的受击半径）又什么时候回去、震屏的时刻和强度、技能专用镜头、打在对手身上的命中特效。
- 伤害公式、目标选择这些逻辑在另一处：`%USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\GameplayAssets\gameplayAsset.data`
  （982 个 JSON / Lua 文件，`tools\gameplay_unpack.py` 解包）。演出时间不在这里。

```powershell
python tools\cab_index.py build _work\cab_index.json          # 只需一次
python tools\skill_sheet.py suit_ines_8 a08 suit_luf_4 g04    # → Assets\ROE\<id>\skill_sheet.json 和 skill_sheet_unity.json
python tools\timeline_dump.py suit_ines_8 --root suit_Ines_8  # 只想看时间轴
python tools\bundle_dump.py gameplay_prefab_suit_ines_8 --list # 看一个包里有什么

# 特效资源：取玩法预制体，依赖会补齐到约 900 个包（共享设置里挂着全游戏的增益特效），导入时只拷这两个单位用得到的 334 个文件
mkdir _work\bundles_skills
copy "<游戏包目录>\gameplay_prefab_suit_ines_8*.ab" _work\bundles_skills\
copy "<游戏包目录>\gameplay_prefab_suit_luf_4.ab" _work\bundles_skills\
python tools\rip.py --bundles _work\bundles_skills --out _work\ripped_skills --log _work\assetripper_skills.log
python tools\import_skills.py suit_ines_8 suit_luf_4           # → Assets\ROE\skills
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeShaderCheck.Run -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeSkillTest.Run -Graphics -Extra '-roeChar','a08','-roeAction','skill1'
```

- **技能表**（`skill_sheet.py`）：把上面这些整理成每个单位一份 JSON，`RoeSkillSheet`（Runtime）读它。对打演示和以后 UFE 的招式数据都从这份表来。
- **导入**（`import_skills.py`）要改写三种引用：
  - AssetRipper 每次导出都给资源新编号，工程里已有的资源（角色模型、音效）按"包名 + 包内路径"对回原来的编号，不重复拷；
  - Timeline、URP 这些 Unity 官方包的脚本，AssetRipper 导出的是空壳，改指向 `Library\PackageCache` 里的真脚本——这样游戏的时间轴文件在这里原样能用；
  - 游戏的着色器换成这里重写的。
- **特效着色器**：六个，都是照反汇编重写的，参数名不变。

| 着色器 | 对应游戏的 | 做什么 |
|---|---|---|
| `ROE/Particles/Default` | Particles/Default | 贴图 × 顶点色 + 自发光；UV 滚动量来自粒子的自定义数据 |
| `ROE/Particles/Dissolve` | Particles/Dissolve | 顶点透明度当溶解进度，按溶解图裁掉 |
| `ROE/Particles/Unlit` | Particles/Unlit | 精简的 URP 粒子着色器：六种和顶点色的混合方式、软粒子、近镜头淡出、序列帧混合 |
| `ROE/Particles/UnlitMaster` | Particles/UnlitMaster | 新的全能版：UV 偏移可给底图 / 溶解图 / 扭曲图，带发光边的溶解，菲涅尔发光，混合方式由材质定 |
| `ROE/Particles/Distortion` | Particles/Distortion | 把身后的画面按法线图推歪（热浪、冲击波） |
| `ROE/Particles/MutateDistortion` | Particles/MutateDistortion | 贴图盖在扭曲过的背景上，可溶解 |

  游戏的两个扭曲着色器在一个单独的阶段画，读的是"已经画完其它特效的画面"；这里读的是 URP 的不透明画面（不含别的特效），所以渲染管线开了 Opaque Texture。
- **播放**（`RoeUnit`）：直接用游戏的单位预制体和它的时间轴。时间轴驱动预制体里的战斗低模，特效挂点有的就在低模的骨骼上；
  低模留着但不画（"替身"），我们的高模每帧按骨骼名抄它的姿势（只抄有动作曲线的骨骼，其余保持高模自己的值）。
  不进播放模式，逐帧 `director.time = t; director.Evaluate()`，粒子由时间轴按时间推进，每次渲染结果一样。
- **对打演示**（`RoeDuel.Run`）用的就是 `RoeUnit`：攻击方播技能的时间轴，冲刺和回位、命中时刻、震屏、音效语音按技能表；
  技能表里"生成在目标身上"的命中特效（g04 的每一下、a08 的 2、3 技能）在对手的挂点上实例化，有自己时间轴的按时间轴推进，没有的直接推粒子。
  `-roeFrames 110-330` 只渲染一段（前面的帧照样计算，粒子状态才对），`-roeSize 960x540` 出小图，`-roeProbe 110,126` 在日志里打印这几帧的骨骼位置。
- `RoeSkillTest.Run`：在影棚里单独播一个技能出几张图；`-roeDump 2` 列出第 2 张图时所有在画的东西和灯，并分别出"只有特效""没有特效"两张；
  `-roeCompare 0.72` 把时间轴给的姿势和直接采样动作片段的姿势逐骨骼比对；`-roeMirror 1` 看镜像后的样子。

踩过的坑（这四个都是看渲染结果、再一层层查出来的）：

1. **特效全挂在世界原点 / 根本不出来。** 时间轴片段用"暴露引用"的名字找场景里的挂点，表在 `PlayableDirector` 上。
   打包后的游戏只保留名字的哈希（Unity 的 `PropertyName` 就是名字的 CRC32），AssetRipper 把这个数字当名字写回去，两边对不上。
   `import_skills.py` 把两边都改成 `roe_<数字>`，并且**按新名字的 CRC32 重新排序** director 上的表——Unity 用二分查找，不排序只能找到一部分。
2. **地面被打得一片橙黄。** 命中特效的粒子带"灯光"模块（亮度 8.3、范围 10 米、最多 15 盏）。照 URP 默认的逐像素算，整块地面都被照亮。
   反汇编显示游戏的着色器只有 `_ADDITIONAL_LIGHTS_VERTEX` 一种：点光、聚光按顶点算，朴素的 Lambert，衰减封顶 1。
   现在角色和场景着色器都照这样算点光、聚光（`RoeCore.hlsl` 的 `RoePunctualVertexLights`），影棚里额外的方向光（补光、轮廓光）仍逐像素。
3. **头和身体错位（像头被打飞）。** 一帧编辑器更新里连续渲染很多帧时，蒙皮网格只在本次更新第一次渲染时算骨骼矩阵，之后一直用旧的，
   于是同一张图里头是这一帧的姿势、身体是另一帧的。给单位下所有 `SkinnedMeshRenderer` 设 `forceMatrixRecalculationPerRender = true`。
   （之前用 `AnimationMode` 采样的版本没碰到，是因为那条路每次采样都会让渲染器重新蒙皮。）
4. 游戏预制体里 Timeline、URP 的脚本必须指向 `Library\PackageCache` 里的真脚本（见上面的"导入"），否则时间轴根本加载不起来。

### 开阔的战斗场景：先粗筛，再挑

s01 只有 7 米宽，做不了能绕圈的 3D 格斗。游戏有 194 个战斗场景（白天、黄昏、夜晚版本几何相同，只算一次，共 168 个），全部导出太慢，所以先粗筛：

```powershell
python tools\stage_survey.py                 # 约 2 分钟：_work\stage_survey\survey.json + 每个场景一张俯视空地图
python tools\stage_survey.py level002 anni   # 只看名字里含这些的
```

- 直接用 UnityPy 读场景包和它引用的网格（不经过 AssetRipper），把每个显示中的网格的三角形按 0.15 米间距采样，放进战斗中心周围 ±20 米、0.25 米一格的俯视网格：
  在战斗中心高度 ±0.2 米、朝上的面算"地面"，0.25～2.2 米高度有东西算"挡住"（墙、柱子、台阶、栏杆）。
- 算出能放下的最大空圆（中心可以偏离战斗中心 4 米以内），俯视图里灰色是空地，红色是障碍，绿圈是最大空圆。
- 结果（半径）：level003_s03 12.6 米、level002_s01 12.2 米、2026_anni_s01 12.0 米……s01 只有 3.5 米。

挑了 5 个导入（`tools\rip_stages.ps1 -Stages '简称=场景名',...`，每个半分钟到两分半；简称和场景名的对照写进 `_work\stage_survey\stages.json`）：

| 简称 | 场景 | 样子 |
|---|---|---|
| `e23_steel_s02` | level_event_2023_realsteel_s02 | **格斗俱乐部擂台**，地上写着 G.O. F.I.G.H.T.，有比分牌和铁栅围栏，半径 8 米。默认用这个 |
| `l003_s03` | level003_s03 | 白色大理石的奇幻广场，最亮最漂亮，半径 12.6 米 |
| `e24_anni_s01` | level_event_2024_anni_s01 | 大理石圆形竞技场，外圈红环，半径 8.2 米 |
| `l002_s01` | level002_s01 | 夜里的日式神社圆形场地（灯笼、旗幡、锁链），偏暗 |
| `e26_anni_s01` | level_event_2026_anni_s01 | 宫殿前的大广场，地面单调 |

`RoeFightScene.Build` 用粗筛的结果定场地中心和半径（射线检测会从细栏杆的缝里漏过去，所以粗筛更准）；没粗筛过的场景再退回到射线检测。

### 基础动作：动作捕捉数据

游戏的角色只有待机、三个技能、受击、倒地，没有走路和拳脚。基础动作用的是公开的动作捕捉数据：
**Bandai Namco Research Motion Dataset 1**（Bandai Namco Research Inc.，CC BY-NC 4.0，非商用、需署名），
https://github.com/BandaiNamcoResearchInc/Bandai-Namco-Research-Motiondataset 。BVH 格式，30 fps，22 个关节，没有手指。

```powershell
python tools\fetch_mocap.py                  # 35 段 BVH + 标注，约 7 MB，存到 _work\mocap\bandai1
python tools\bvh_preview.py _work\mocap\bandai1\dataset-1_kick_normal_001.bvh --out sheet.png   # 火柴人连环图 + 手脚速度
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMocap.ImportAll            # → Generated\mocap\mc_*.anim + mocap_strikes.json
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMocap.Preview -Graphics    # 两个角色做每个动作的检查图
```

- 用到的段落：`punch_normal_001` 前手刺拳（也取它开头的拳击架势当防御姿势）、`punch_normal_002` 后手直拳、`kick_normal_001` 回旋踢、
  `slash_normal_001` 挥砍，走、退、跑用 feminine（女性化）风格。
- 转换器 `RoeMocap` 现在是通用的（骨骼名自动识别、单位自动判断），别的 BVH 数据写一个动作包 JSON 就能用，见"动作包"一节的第二个包。
- **T 姿势**：BVH 的零姿势是"每根骨头都沿自己的 x 轴"，不能直接建人形骨架。做法是取一帧自然站立，把骨盆转正，再把每根骨头用最小摆动
  （不加扭转）转到 T 姿势方向——脊柱朝上、手臂水平朝外、腿朝下、脚尖朝前——然后用这个姿势建 Unity 的人形映射。
- 每帧把 BVH 摆到骨架上（右手系转左手系：x 取反，绕 y、z 的角度取反，厘米变米），用 `HumanPoseHandler` 读出肌肉值写成人形动作。
- **对齐**：出招的段落按"出手方向"转到正前方（击打部位伸得最远时，从骨盆指向它的水平方向），拳击架势和刺拳用同一个转角；
  走、退、跑按整段的平均朝向；循环动作在指定范围里找首尾最像的两帧切出一个周期，并把匀速前进的位移从片段里扣掉，原地踏步（位移由格斗逻辑给）。
- **招式表** `mocap_strikes.json`：每招的击打骨骼、最远够到哪（刺拳 0.67 米、直拳 0.92 米、踢 1.04 米、挥砍 0.76 米）、出手高度、
  有效时间段（击打部位在最远距离 85% 以内的那段）。
- **侧步**：数据集里的 walk-left/right 是身体转 70 多度往侧面走，不是面朝前侧跨步。先试过以拳击架势为底生成侧步片段
  （`side_left/right`，两腿交替外展抬腿，还在 `Generated\mocap` 里），但片段的步幅追不上格斗逻辑移动身体的速度，脚在地上滑 1.5 米/秒；
  现在侧步时上半身用拳击架势，腿由代码迈步（见"手臂和腿"一节）。

### 格斗：能玩的版本（UFE 到之前）

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics [-Extra '-roeStage','l003_s03']   # → Assets\RoeFighter\Scenes\Fight_<场景>.unity
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeSeconds','150'       # 电脑对电脑，出帧和 timeline.json
python tools\make_video.py _work\fight _work\fight\fight.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics                             # → out\ROEFighter\ROEFighter.exe
```

双击 `out\ROEFighter\ROEFighter.exe` 就能玩（窗口 1600×900，可以拉大；整个文件夹 703 MB，四个角色以后 1.1 GB，打包 1.5–4.5 分钟，不能单独拷 exe）。开局先进选人界面（见"选人界面"一节）。
**游戏说明**（10-11，给玩的人看：怎么操作、规则、每个角色的招式）：`docs\game-manual.md`；exe 旁边的 `out\ROEFighter\游戏说明.html` 是带截图的版本，
由 `python tools\make_manual.py` 从它生成（键盘图、角色总览自动画；截图用 `--shot select=<帧> --shot fight=<帧> --shot skill=<帧>` 换）。改了按键或规则时一起改这份说明。
也可以在编辑器里打开 `Assets\RoeFighter\Scenes\Fight_e23_steel_s02.unity` 按 Play。默认 1P 是人、2P 是电脑。
第一次打包失败过一次：Unity 自带的 `MovedFromExtractor` 处理 `UnityEngine.IMGUIModule.dll` 时退出码 3、没有任何输出，原样重跑就成功了。
按键：

| | 1P | 2P |
|---|---|---|
| 左右移动（朝对手走 / 后退，按住后退 = 防御） | A / D（或手柄摇杆） | ← / → |
| 侧步（往画面里 / 往画面外，绕着对手转） | W / S | ↑ / ↓ |
| A–D 四个攻击（`bandai1`：刺拳、挥砍、直拳、回旋踢；`accad_male2`：刺拳、前腿回旋踢、直拳、后腿回旋踢；Fiona 用自己的：突刺、反击斩、转身斩、踢加斩；Inase 用 Mecanim Bot 的：刺拳、中段前踢、冲步直拳、回旋高踢） | J、K、U、I（手柄 0–3） | 小键盘 1、2、4、5 |
| 换动作包（比赛重新开始） | F3 | |
| 换物理方案（各用自己游戏的 / Magica 式骨骼布料 / DOA6 的物理 / 只有胸用 DOA6 / DOA5LR 式弹簧网 / Vindictus 的 KawaiiPhysics / 剑星的物理 / 10-02 旧版 / 关 / 装了插件时还有 Magica Cloth 2 和 ROE 自己的（roe）；比赛重新开始，见 `docs/doa-physics.md` 第 4 节） | F4 | |
| 裙子：自然下垂 / 跟腿走（立即生效） | F5 | |
| 爆衣开 / 关（关掉时立刻全部穿回去；`ROEFighter.exe -roeBurst 0` 以关闭状态启动） | F6 | |
| 技能 1、技能 2、超必杀（要满能量槽）：ROE 角色的就是她游戏里的三个战斗技能 `skill_01..03` | 1、2、3（主键盘的数字键，像 ROE 的三个技能按钮；10-11 起），或 L、O、P，或 236+J/U、214+J/U、236236+J/U | 小键盘 3、6、9 |
| 电脑接管 1P / 2P | F1 / F2 | |
| 选人界面：换卡片 / 确认 / 退回（只有 1P 是人时，先选自己的，再替电脑选） | A、D / J（或回车、空格）/ K | ←、→ / 小键盘 1 / 小键盘 2 |
| 回选人界面（比赛结束后按回车也回到这里，上一场的两人还选着，确认两次就是再来一场） | F7 | |
| 镜头：避开场景 / 旧镜头（只管构图，会跑到栅栏外面；`ROEFighter.exe -roeCamAvoid 0` 以旧镜头启动） | F8 | |

结构（`Assets\RoeFighter\Runtime\Fight\`）：

- `FighterRig`：一个角色的"身体"。人形高模由一个可播放图驱动，分三层：
  0 层永远播游戏的战斗待机（头发、扇子、裙子、武器这些非人形骨骼的值从这里来）；1 层是动作捕捉的基础动作（只动人形骨骼）；
  2 层是游戏自己的动作（受击、倒地、技能、开场和胜利姿势，所有骨骼都动），播它们时整层淡入盖住下面。
  每层里的片段权重每步都写进混合器并归一化（见下面"踩过的坑"第 5 条）。姿势算完之后，按动捕层的权重再做三件事：
  动捕着地、侧步时腿自己迈步、驱动肢体辅助骨（见"手臂和腿"一节）。
  游戏的单位预制体跟在同一个位置，放技能时它的时间轴和高模同步推进，特效挂在它隐藏的低模骨骼上，出现在游戏原本的位置；
  技能在最后一下伤害 0.8 秒后就放开身体（淡回架势 0.3 秒），时间轴在后台播完，特效自然消失。
- `Fighter`：状态机。中立（待机、走、侧步，总是面向对手）、出招（出招时朝向锁定，侧步能躲开）、防御、受击、倒地起身、技能、KO、胜负。
  打中或被挡住之后，从招式的取消点起可以接下一招。技能按技能表冲到对手面前、在游戏规定的时刻造成伤害（按游戏的比例分段）。
- `FightGame`：固定每秒 60 步。每步：输入（键盘、手柄或电脑）→ 两个角色的状态机 → 摆姿势 → 命中判定（击打骨骼的球体对对手身体的竖直胶囊）
  → 技能伤害 → 特效 → 回合规则 → 镜头。打中有 4–6 帧的顿帧和小幅震屏；KO 时慢动作。三局两胜，每局 60 秒。
  技能的第一下决定结果：对手按住后退就是挡住（只掉 10%），走出了范围就是打空，否则命中；挡住或命中后对手被锁住直到技能结束（游戏的演法）。
  镜头从两人连线的侧面拍，离得够远装下两人；10-05 起只站在场地的空处、看两人的视线不被挡（`CameraRoom`，见"镜头"一节）。
- `FightAI`：电脑对手每 0.1–0.25 秒想一次：保持距离、按距离挑招、看到对方出招时有一定概率防御或侧步、偶尔放技能、满槽放超必杀。
- `FightHud`：血条（掉血有红色的滞后条）、计时、回合标记、能量槽、中间的大字（ROUND 1 / FIGHT! / K.O. / 谁赢）。录像时由镜头画进画面。
- `RoeFightProbe`（编辑器）：查问题用的几个探针——每个角色在画什么、时间轴绑定在哪、同一时刻"可播放图"和"直接采样"的姿势对比、技能全程的腿长；
  `MatchAt` 按录像同样的种子和步数重放比赛到指定的步，打印混合器里每个片段的实际权重、蒙皮后掉到脚下面的顶点和它们挂的骨头，并拍图；
  `MovesSheet` 拍两个角色每个基础动作关键时刻的侧面和正面特写；`FootSlide` 测走路和侧步时着地的脚滑多快。
- 声音：普通攻击用每个角色最短的那条技能命中音效（之前取到的第一条偏重）；比赛时循环播放场地自带的战斗音乐（格斗俱乐部是
  `bgm_event_realsteel2023_battle`），录像时写进 `timeline.json` 的 `music`，`make_video.py` 循环混在音效下面。

踩过的坑：

1. **扇子在出拳时张开成巨大的扇面。** 动作捕捉的片段没有扇子骨骼的曲线；如果和游戏的片段放在同一个混合器里，混合器认为扇子"被动画了"，
   动捕片段那一份就取默认值（绑定姿势，也就是完全张开的扇子）。所以游戏片段单独放一层，不播游戏片段时那一层权重为零。
2. 游戏的技能动画很长（3–7 秒），最后一下之后还有收招和回原位（1–5 秒）；照原样播，放技能的人这段时间无敌，对手却已经能动。
   现在最后一下 0.8 秒后就结束（技能的回原位滑步若在最后一下 1.5 秒内结束，就等它滑完），对手被锁住的时间按这个结束时刻算。
3. 数据集的 walk-left/right 不是侧步（见上一节）。
4. 每次开机后，命令行跑 Unity 要先把 Unity Hub 开着，否则许可证拿不到（"Access token is unavailable"）。
5. **从第二回合起，g04 放技能时浮在半空，小腿变成一直拖到地面的尖刺。** 每回合开始时用"淡入时间 0"切到开场动作，
   旧代码只改了权重数组、没把归零的权重写进混合器，下一步 `Tick` 又跳过权重已经为 0 的片段——上一回合的胜利动作 `idle_02`
   就以权重 1 一直留在游戏动作层里。人形混合器不会归一化，两个权重 1 的片段相加：髋的高度翻倍（离地 0.9 米），
   每根辅助骨离父骨的距离也翻倍（小腿扭转骨跑到脚下面，皮肤被拉成尖刺）。第一回合不会出现，所以单独测技能时一直复现不了；
   `RoeFightProbe.MatchAt` 按同一种子重放到出事的那一步，打印混合器的实际权重才看出来。现在每步都把所有片段的权重写进混合器，并在每层里归一化。

### 手臂和腿：权重、辅助骨骼、着地、迈步

看了第一版录像，手臂和脚的动作都不太对。逐项查下来是三个问题（再加上一节"踩过的坑"第 5 条的权重叠加）。

**1. 皮肤挂在辅助骨上，动捕动作驱动不到。** `RoeWeightReport` 统计每个网格的权重：

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeWeightReport.Run     # 每个网格、每根带皮肤的骨头
```

网格本身没毛病：每个顶点最多 4 根骨，权重和都是 1，没有缺失的骨头。问题在于手臂和小腿的皮肤大部分挂在辅助骨上：

- g04 的上臂、前臂、小腿这三根人形骨一个顶点都没带。前臂的皮肤在 `ForeTwist`（408 个顶点）和 `ForeTwist1`（469 个）上，这两根骨的父骨是**上臂**；
  小腿的在 `CalfTwist` / `CalfTwist1` 上，父骨是**大腿**；上臂靠肘的一段（`LUpArmTwist1`）挂在锁骨下面的一条链上。
- a08 也是这样，只是人形骨上还带着一部分。

游戏的每个动作都逐帧给这些辅助骨写了旋转，所以在游戏里看不出问题。动捕动作只驱动人形骨，辅助骨就停在待机姿势里相对父骨的角度。
肘和膝一旦弯得和待机不一样，前臂和小腿的皮肤就跟不上：出直拳时手臂像一截折断的细棍，踢腿时靴子和小腿脱开。

做法：从游戏自己的动作里把辅助骨的规律拟合出来，放动捕动作时按这个规律驱动。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.Run       # → 人形预制体上的 RoeHelperRig 组件
```

- 在原版通用骨架上采样游戏的全部自带动作（待机、开场、三个技能、受击、倒地，约 450 帧，每秒 15 帧）。
- 对每根带皮肤的肢体辅助骨，把每根人形骨都试一遍，看它是不是"跟随的骨"：辅助骨在那根骨的坐标系里应该是固定的，最多绕骨轴扭一点。
  扭转量按"下一根骨（手、脚）的扭转 × 比例 + 上一根骨的扭转 × 比例 + 常数"做最小二乘。g04 转扇子时手腕会转过半圈、角度绕回来，
  这些离群帧剔掉后再拟合。
- 膝盖、肘部的辅助骨再试"两根相邻人形骨之间按比例插值"。
- 位置：找它保持固定偏移（1 厘米以内）的那根人形骨，没有就交给父骨。
- 战斗中每步动画算完后，`RoeHelperRig.Apply` 按动捕层的权重驱动这些骨。放游戏自己的动作时，照用游戏的数值。

拟合结果（误差是和游戏原动作的差，去掉最差的 10% 帧）：

| 辅助骨 | 跟随 | 扭转 | 误差 |
|---|---|---|---|
| `ForeTwist`（前臂靠肘） | 前臂 | 手腕扭转的 0.33 | 4–10° |
| `ForeTwist1`（前臂靠腕） | 前臂 | 手腕扭转的 0.64–0.67 | 5–11° |
| `CalfTwist` / `CalfTwist1` | 小腿 | 脚的扭转的 0.32 / 0.6 | 6–8° |
| `knee_L/R` | a08：大腿到小腿插值 0.55–0.6；g04：跟小腿 | — | 4–7° |
| `muscle_elbow_L/R` | 上臂到前臂插值 0.75–0.8 | — | 4–5° |
| `LUpArmTwist1` / `RUpArmTwist1`（g04） | 上臂 | — | 0° |
| `ThighTwist`（a08） | 大腿 | — | 4–5° |

误差偏大的有：a08 的护肩 `Scapular`（17°）和肩上两个球形护甲（14–16°），g04 的右上臂扭转骨（20°）。
这几根在游戏里还有别的驱动（护甲摆动、扇子招式），先按最接近的规律跟随。

**2. 动捕动作悬空 11–18 厘米。** 人形重定向按身高比例算髋的高度。这两个角色腿长、穿高跟，动捕演员是平底鞋，
站架时脚踝离地 0.31 米，而游戏自己的待机里只有 0.15 米。

做法：开局时从游戏的站姿里量出脚尖骨和脚踝骨离地多高，作为"鞋底"。动捕层每步取两只脚上最低的鞋底点，把整个身体竖直挪到它正好贴地。
游戏自己的动作不做这一步。

**3. 滑步。** `RoeFightProbe.FootSlide` 让两个角色在真实的战斗逻辑里前进、后退、往两边侧步各 3 秒，测"接触点"在地上滑多快
（接触点 = 每步里两个脚踝、两个脚掌中最贴近各自着地高度的那个；连续两步都是它、且离着地高度 2 厘米以内才算）：

| | 修之前 | 修之后 | 再加上第 4 条（鞋底摆正） |
|---|---|---|---|
| 前进 | 0.7–0.8 米/秒 | 约 0.3（脚掌绕脚踝转，这种走法脚尖外撇；没有净漂移） | 0.17–0.20 |
| 后退 | 0.3 | 0.07 | 0.09 |
| 侧步 | 1.5–1.7 | 0.00–0.02 | 0.01 |

- **侧步**：上半身用拳击架势，腿由代码迈步（`FighterRig.SideStep`）。着地的脚钉在落点不动；它落后于"身体下方该在的位置"超过半步时，
  就抬起来沿一道弧线（0.17 秒、抬高 7 厘米）落到前方，落点选在下一次着地的中段正好回到身体下方。两脚交替，先迈前导脚。
  腿用两节骨 IK 弯过去，膝盖保持原来的朝向。身体移动多快，步子就迈多快，脚不会滑。
- **走路**有三步：
  1. 每个角色开局时把走路、后退、跑步的动作原地放一个周期，量出着地的脚掌相对身体往后移多快（步速）。
     a08 前进 0.76、g04 0.78 米/秒，后退都是 0.56。动作按"移动速度 ÷ 步速"的倍率播放。
  2. 动捕走路里着地脚相对身体的速度在一步之内并不均匀（0.7–1.3 米/秒），所以身体不匀速移动：每步姿势算完后，
     看着地的那部分脚（脚掌或脚踝）在这一步里相对身体往后移了多少，就把身体往前挪多少。只沿走路方向挪，
     最多是设定速度的 2.5 倍。身体自然一顿一顿地走，平均速度还是设定值。
  3. 重定向到长腿骨架后，落脚那几帧脚还会贴着地往前蹭。最后一步是脚步锁定：动画判定某只脚着地时，把它的落点锁住，
     用同样的 IK 把腿弯过去；动画把脚抬起来时松开，脚离落点超过 25 厘米时也松开。第 2 步读的是锁定之前的动画姿势，两者不会互相推着走。
- 坑：测步速是在 `Init` 里放动作测的。上一局留下的脚步锁定开关如果没复位，脚被锁在地上，步速就测小了，后退一下子变成 3 倍速。

**4. 高跟鞋：站着时踮着脚尖。** 用户看了之后说 Inase 的脚还是不对。正面镜头看不清，写了 `RoeFightProbe.Feet`：在真实的战斗逻辑里
从侧面贴着地面拍一个角色的脚，并逐帧量每只脚的倾角、脚踝和脚趾骨的高度，以及蒙皮后鞋子"后半（鞋跟）"和"前半（前掌）"最低点离地多高。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.Feet -Graphics [-Extra '-roeChar','g04']   # → _work\feet\*.jpg + feet.txt
```

- 游戏自己的站姿（开场动作）是基准：脚的倾角 −71°（脚踝到脚趾骨朝下 71°），鞋跟和前掌最低点都在地面 ±2 厘米内。
- 动捕站架里，鞋跟离地 **10–11 厘米**、前掌插进地面 2–3 厘米：等于踮着脚尖站，另一只脚还常悬空 1–3 厘米。
  动捕演员穿平底鞋，脚踝角度照搬到这双高跟凉鞋上，脚就往前倾了；前掌（脚趾骨）也是平底鞋的弯法。
- 做法（`FighterRig.Tick`，只在动捕层生效，游戏自己的动作不动）：
  1. `Init` 时从站姿里记下每只脚相对"它在地面上指的方向"是怎么摆的，以及脚趾骨的局部旋转。
  2. 每步先按原样找出最低的鞋底；脚踝离自己站立高度不到 3 厘米的脚，换成站姿的摆法（方向仍然跟动作走），
     3–12 厘米之间渐变回动作自己的角度。迈步、踢腿抬起来的脚保持动作原样。脚趾骨一律用站姿的。
  3. 再把最低的鞋底放到地面上。
  4. 另一只脚如果几乎不动（每秒不到 0.35 米）、离地不到 5 厘米，就用两节骨 IK 放到地上。
- 结果：着地的脚鞋跟和前掌都贴地，误差 0.4 厘米以内（a08、g04，两个动作包都一样）；滑步也比之前小（见上表）。

### 布料：骨骼布料（照 Magica Cloth 2 的做法）

ripper_tpose 仓库里另一个窗口做了 Blender 插件 `scripts/blender_addons/bone_cloth`：照 Magica Cloth 2 的 BoneCloth 模拟骨链，烘成 MMD 动作的关键帧。
它的解算器（`core.py` 的 `simulate()`）不依赖 Blender，这里原样移植成运行时的 `RoeBoneCloth`，每步在姿势算完后跑。

- **原理**：以动画姿势为基准，每节骨头的尾端是一个质点。每个子步依次做：
  1. **惯性**：质点跟着挂点骨（裙子是骨盆，头发是头）走，只有一部分身体运动会被布"感觉"到，而且不超过限速；
  2. Verlet 积分：阻尼、限速、重力；
  3. 从根到梢逐节约束：
     - **角度恢复**：往动画方向拉回一部分，拉回的位移大部分不变成速度；
     - **角度限制**：根部严、梢部松；
     - **碰撞体**：大腿、小腿、两胯的胶囊。骨段整段当胶囊去碰，不会从两腿之间钻过去；动画本身就压进碰撞体的那部分不算；
     - **背挡**：不许往身体那边退到动画位置后面；
     - **地面**。

  骨长是刚性的。我们每步 1/60 秒，拆成 2 个子步，等于 120 Hz（插件和 Magica 是 90 Hz，参数按每 1/90 秒换算）。
- **哪些骨头**：按名字找，分四类，参数用插件的预设：
  - 裙子 `skirt`：比插件的"裙子"预设硬（角度恢复 0.35、阻尼 0.15、角度限制 5°/35°、惯性 0.3，见下面"g04 的裙子不自然"）；只有一节的裙饰不模拟；
  - 头发 `hair / braid / bangs / hari`：插件"头发"预设，碰躯干胶囊；
  - 链子和饰物 `skirt_chain / chain / chain_arm / dec_wrist`：更松的一组参数；
  - 胸 `breast / chest_L/R`：更有弹性、角度很小。

  有两个以上同类子骨的骨头本身不模拟，它下面的链挂在它上面。a08 是 9 组 83 根，g04 是 5 组 64 根。扇子、武器、脸上的骨头不碰。
- **碰撞体**：大腿、小腿的胶囊从网格量粗细。取"头部落在这一段上的所有骨头"（包括扭转辅助骨）带的顶点，到骨段距离的 70% 分位数：
  a08 / g04 大腿约 8.6 → 6.4 厘米，小腿约 5.4 → 3.9 厘米。
- **什么时候生效**：动捕的基础动作时全开（`FighterRig.clothWeight`，默认 1）；游戏自己的技能、受击里，裙子和头发是游戏手调的关键帧，保持原样。
  没有动画曲线的骨头（a08 的裙链、剑链）每步动画求值前先回到静止角度，否则会把上一步的模拟结果当成动画基准。
- 演示：`RoeClothDemo` 让每个角色用真实的战斗逻辑把同一段动作做两遍（站架、前进、后退、两边侧步、刺拳、直拳、踢、挥砍），
  一遍关布料、一遍开，`tools\cloth_demo_video.py` 左右拼成 `out\bone_cloth_demo.mp4`。踢腿时，关布料的腿直接穿过裙片；开布料的裙片被腿顶起来。
- 坑：同一个 Unity 会话里把场景重新打开，URP 的后期处理会丢掉 Volume 组件（断言 `SetupColorLut colorAdjustments cannot be null`），
  从第二次开始每帧都是黑的。演示工具只开一次场景，每组之间重新 `Setup`。
- **裙子的基础姿势（10-02 晚）**：用户说 Inase 的裙子还是不对。`RoeFightProbe.Skirt` 从前、左、后三面拍胯和腿：游戏自己的动作里
  两片长裙摆从胯两侧垂到脚踝；动捕动作里却像两块硬板斜着向外、向后翘出约 45°，关掉布料也一样，所以问题在布料模拟之前。
  裙子根骨挂在骨盆上，关键帧来自第 0 层的游戏待机动作，是相对骨盆的角度；`RoeFightProbe.PelvisCheck` 量出待机动作里骨盆明显侧倾
  （右胯比左胯高约 23°），动捕的骨盆是正的，裙子就跟着骨盆转偏了同样的角度。
  做法：`Init` 时从站姿里记下骨盆上每片裙子的根骨（下面挂着链的子骨；a08：`Skirt_L_00`、`Skirt_R_00`、`Skirt_M_00/01`；
  g04：`Skirt_Back_01`、`Skirt_front_01`、`Skirt_Left_01` 等）相对"胯部朝向"（由两条大腿的连线定）的世界旋转。
  动捕站架时它们就是这个旋转（和游戏站姿一样垂下）；骨盆偏离站架时的转动（走路摆胯、踢腿），它们跟着转。
  站架的骨盆朝向在 `Init` 里把站架片段放一圈取平均。
- **g04 的裙子不自然（10-02 晚）**：用户接着说 Luffee 的裙子不自然。查出三件事：
  1. 上一条第一版让裙子根骨完全不跟骨盆倾斜。`RoeHelperFit.SkirtReport` 在游戏全部自带动作里比较几种跟随方式（单位：度，取误差最小的 90% 帧）：
     g04 腰间的两块小饰片几乎刚性跟骨盆（3–10），按"只跟胯部朝向下垂"算是 22；后片、装饰条也是跟骨盆最准（11–16 对 24）。
     所以改成上面说的"站架时校准、之后跟骨盆转"；没有下级骨的小饰片完全不动，刚性跟骨盆。
  2. 布料参数（插件的裙子预设：角度恢复 0.2、阻尼 0.05、角度限制 25°/60°、惯性 0.4）对 g04 太软：她每片裙子从腰上第一节就开始模拟
     （a08 的根骨下分成几条链，根骨本身钉在腰上）。`RoeFightProbe.SkirtSwing` 量每条裙链末端偏离动画姿势多少、一帧最多转多少：
     g04 站着不动平均偏 12°、一帧最多跳 14°（动画本身一帧只变 4°），走路、出招时平均 16–22°、一帧最多跳 20–39°；
     腰饰小片也在模拟，一直顶在 25° 的限制上来回跳。
     现在裙子用角度恢复 0.35、阻尼 0.15、角度限制 5°/35°、惯性 0.3，一节的裙饰不模拟。
  3. 腿把裙片推开时，推开的位移几乎全变成了速度（只扣 5% 摩擦），裙片被腿弹飞；骨段的碰撞点靠近根部时，末端还会被推出穿透深度的最多 5 倍。
     现在推开的位移九成不变成速度，杠杆最多 3 倍，一次最多推 2.5 厘米。
  结果（g04，Bandai 包，平均偏差 / 一帧最大跳动）：站架 12°/14° → 4.6°/7°；前进 16°/20° → 6.5°/17°；侧步 17°/25° → 8°/9°；
  直拳 22°/39° → 8°/28°（最大的是一条细装饰线）；后腿踢 22°/32° → 9°/16°。a08 也更安静（站架 3.5° → 1.6°）。
  对比视频 `out\g04_skirt_fix.mp4`（左：修改前，右：修改后）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.Skirt -Graphics [-Extra '-roeChar','g04']        # → _work\skirt\<包>_<布料>_<时刻>_<方向>.jpg
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtSwing -Graphics -Extra '-roeChar','g04','-roeMotions','bandai1'   # 摆动的数字
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.SkirtReport                                     # 游戏怎么动裙子根骨
```
  `SkirtSwing` 还能带 `-roeSkirt 'restore=0.3,limitRoot=8'` 临时换裙子参数、`-roeVariant nocoll / noback / nohip` 去掉碰撞体、背挡、根骨修正，用来对照。
- **Magica Cloth 2 本身是怎么做的（10-02 晚调研）**：用户看了上面的对比视频说"不行，还是有问题"，于是先查清 Magica 的做法，全文在
  [`docs/magica-cloth-2.md`](docs/magica-cloth-2.md)。要点：
  - 底子和我们一样（骨骼当质点，沿基线做角度恢复、角度限制，加惯性、碰撞、背挡）。但 Magica 能把相邻骨链横向连成网，
    一片布的几条链之间有距离、弯曲约束，横边也参加碰撞；我们只有各自独立的链。
  - 查蒙皮：a08 每侧的长裙是一整片布挂在前、侧、后 3 条链上，g04 的前片挂在 2 条链上。视频里 g04 前片下摆折起、卷边，就是两条链各摆各的。
  - Magica 对裙子的头号建议是让"动画姿势"跟腿走（美术蒙皮、逐个动作调，或它的 Custom Skinning），模拟叠在上面；
    我们动捕动作里的基准是站架里的裙子钉在骨盆上，腿每一步都插进去，全靠碰撞推开。
  - 游戏自己的 95 个 Magica 组件全是大厅里的头发模板（Line、惯性 0），战斗里的裙子是手调动画，照搬游戏的设置解决不了。
  - 改法建议：裙子的动画姿势跟腿走（用游戏手调的裙子动画标定蒙皮权重）、一片布的链横向连起来、惯性分世界和局部；或者买 Magica。
  - 数据：`python tools\magica_survey.py settings | links <角色> | picture <图>`；图 `out\skirt_sheets_1002.png`。
- **照 Magica 的做法重做（10-02 晚）**：用户选了上面的方案 A，"做成可插拔的方式，先用A，后续我想办法买到 Magica 再切换"。细节和全部数字在
  [`docs/magica-cloth-2.md`](docs/magica-cloth-2.md) 第 9 节。
  - **可插拔**：布料后端接口 `IRoeCloth`（`Runtime/RoeCloth.cs`），游戏里 **F4** 依次换：Magica 式骨骼布料（默认）/ 10-02 旧版 / 关。
    10-04 扩成按类别分配解算器的"物理方案"（各用自己游戏的、DOA6 的……），见"霞（DOA6）和可插拔物理"一节。
    Magica Cloth 2 的适配器 `Runtime/RoeMagicaCloth.cs`（同样的分片、参数、腿部胶囊）：10-04 晚插件装上后能用了，见下面"Magica Cloth 2 装上了"。
  - **裙子的动画姿势跟腿走**（`RoeSkirtRig`，`RoeHelperFit.FitSkirt` 拟合）：每节裙骨的尾端由胯部朝向、骨盆、两条大腿、两条小腿按权重带着走，
    权重用游戏手调的裙子动画拟合。在拟合没用到的游戏动作上，裙骨方向偏离游戏关键帧：a08 35.5° → 31.1°，g04 47.2° → 37.2°（剩下的主要是美术加的摆动，由模拟补）。
    基准和拟合时一样是游戏站姿：骨盆、大腿、小腿从站姿里动了多少，权重在它们身上的裙骨就跟着动多少（第一版的基准是动捕站架，见下面"屁股附近翘起来"）。
  - **一片布连成网**：从蒙皮自动找跨链的三角面，a08 每侧前、侧、后 3 条链连起来，后腰两条短片连起来，g04 前片两条链连起来；横边保持距离、也参加碰撞。
  - 另外：惯性分世界（角色在场上移动）和局部（动画），Tether，离动画姿势的最大距离，重置后 0.1 秒稳定，碰撞只放过站姿里本来就有的深度。
  - 结果（`SkirtSwing`，各段动作"裙子插进腿部胶囊多深"的平均，括号里是最深，厘米；胶囊按皮肤 70% 分位数量，比腿略粗，所以不是看得见的穿模深度，只用来比较）：

    | | 旧版 | 新版第一版（基准：动捕站架） | 新版（基准：游戏站姿） |
    |---|---|---|---|
    | g04，Bandai 包 | 7.5（9.1） | 2.2（5.8） | **1.9**（6.2） |
    | g04，ACCAD 包 | 6.8（9.6） | 1.8（3.4） | **1.3**（2.9） |
    | a08，Bandai 包 | 6.9（8.5） | 4.6（7.1） | **2.9**（8.2） |
    | a08，ACCAD 包 | 7.6（8.4） | 4.8（8.2） | **3.7**（6.4） |

    一帧里裙摆最大的跳动差不多（旧 15–28°，新 14–24°）；横边在踢腿时还会被拉长，现在最多 40%（第一版 35–59%）。
  - 看图：`out\skirt_compare_g04.jpg`、`out\skirt_compare_a08.jpg`（旧版 / 新版，正面、侧面、背面，前进、后退、侧步、踢、直拳）；
    视频 `out\skirt_magica_style.mp4`（46 秒，左旧右新，Bandai 包和 ACCAD 包，两个角色）。g04 回旋踢时，旧版前片竖直穿过抬起的大腿，新版搭在大腿上。
- **屁股附近翘起来（10-02 晚）**：用户问"还是裙子，接近屁股的部分翘起来了一点点，是什么刚体还是什么设置得有问题么"，"尤其是luffee"。
  - 不是刚体，也不是布料参数：F4 关掉布料一样翘，问题在模拟之前的基准姿势。
  - g04 的后片在游戏里几乎钉在骨盆上动（拟合权重：骨盆 0.92）。游戏站姿的骨盆是歪的（右胯高约 23°），后片搭在右边屁股上。
    第一版把骨盆、腿的基准放在动捕站架，站架里裙子和站姿里一模一样（相对胯部朝向）；可动捕站架的骨盆和站姿的差 25–31°（`RoeFightProbe.SkirtRests`），
    屁股跟着骨盆转了、后片没跟着转，后片上端就离开了屁股。
  - 改法：基准也用游戏站姿（`RoeSkirtRig.RestOnStance`，默认开），和拟合时一致，骨盆转多少，钉在骨盆上的后片就转多少；
    挂在胯上的裙根也改用站姿的骨盆做参照，否则裙根和从它下面指出去的骨对不上。a08"站架里裙子本来就伸进腿 3.7 厘米"其实就是这处没对齐。
  - `RoeFightProbe.ButtGap` 把皮肤烘出来，在髋关节下方 5 厘米处量后片内侧到屁股的距离（厘米，正数 = 中间有空气）：

    | g04 | 站架 | 前进 | 后退 |
    |---|---|---|---|
    | 第一版（基准：动捕站架） | 3.4 | 4.8 | 3.0 |
    | 现在（基准：游戏站姿） | **0.6** | **1.8** | **0.4** |

    基准姿势插进腿多深（`SkirtDepth`，Bandai 包，平均厘米）：a08 站架、前进、后退、跑都从约 3.5 降到 0.3–0.6；g04 前进、后退、跑多了约 1（5.3 → 6.3），模拟之后反而更浅（上表）。
  - 看图：`out\skirt_butt_g04.jpg`（右侧、右后特写，上面一行是游戏原版站姿做参照），`out\skirt_rest_a08.jpg`（a08 正面、侧面、背面，没有翘回 45°）；
    视频 `out\skirt_rest_compare.mp4`（23 秒，从右后方跟拍胯部，左旧右新，两个角色）。
- **自然下垂（10-03）**：用户看了上一条之后问"能自然下垂么，还是有一点翘"。
  - 原因：跟腿走的基准姿势是游戏站姿里的裙子被腿带着走。游戏站姿前倾、屁股往后翘，裙子顺着那个身体斜着挂，换到动捕比较直的身体上就斜着翘出去：
    没贴身体的裙骨平均偏离竖直 17–23°（游戏站姿本身也有 16–20°，`RoeFightProbe.SkirtHang`）。
  - 做法（`RoeSkirtRig.Drape`，默认开；游戏里 **F5** 在"自然下垂 / 跟腿走"之间切，屏幕上方显示 `SKIRT: hangs / fitted`）：
    1. 身体表面（`Runtime/RoeBodySurface.cs`）：开局时从蒙皮网格取胯到脚的皮肤点（胯部只取皮肤材质，腰带饰物不算；腿上连护甲、鞋都算），2.5 厘米取一个，
       只留朝外的面（护甲、鞋的内侧面会把裙子往里拉）；每一步按骨骼重新蒙皮，约 1400 个点，每个角色每步约 0.5 毫秒。
       离皮肤多远用附近多个点的切平面距离按高斯权重平均（不取"最近一个点"：最近点会在两块皮肤之间来回跳，裙子一帧跳几十度）。
    2. 每节裙骨从上一步的方向往竖直方向落（时间常数 0.05 秒；每步从头算会在腿的两侧之间跳），碰到皮肤就沿皮肤法线推开：
       骨的中线离皮肤 1.2 厘米加这节布本身的厚度（下摆的毛边有几厘米厚）。只算朝这条链那一侧的皮肤（前片不会被推进两腿中间），抬起的大腿从下面托住。
    3. 贴着身体的平布绕自己的骨转一下，平贴在皮肤上（最多 60°）：平布斜着压在圆屁股上，一边会陷进去。
    4. 每条链下半段（链长 45% 以下）再拿这节布自己的 24 个顶点去碰皮肤，最深的那个决定推多少（每轮最多 2 厘米，最多把骨抬到偏离竖直 45°）：
       g04 后片下摆的毛边是从侧面包住脚后跟的，骨的中线离腿还有 6–7 厘米。上半段不这么做：为了一个角把整片后片从屁股上推开，又翘起来了。
    5. 布料模拟：这样挂好的裙子在腿部胶囊（比腿略粗）里允许和基准姿势一样深，不然胶囊又把它推出去；裙片之间的横边不再单独碰胶囊（和横边长度打架，a08 一步抖 13°）。
  - 数字（Bandai 包，布料开）：没贴身体的裙骨平均偏离竖直（站架 / 前进 / 后退）a08 20 / 19 / 23° → 11 / 8 / 9°，g04 17 / 19 / 19° → 11 / 15 / 14°；
    裙子网格陷进皮肤的顶点 a08 0 / 0 / 3% → 0 / 0 / 0%，g04 0.3 / 0.8 / 2.0% → 0.1 / 0.9 / 0.8%；g04 后片上端贴着屁股（0.6 厘米 → 贴住）。
  - 看：`out\skirt_drape_compare.mp4`（右后方跟拍胯部、再从正面，左上一版 52f11de，右自然下垂，两个角色），`out\skirt_drape_g04.jpg`、`out\skirt_drape_a08.jpg`。
- **Magica Cloth 2 装上了（10-04 晚）**：用户下载了插件，"之前riseoferos的布料都不完美，尽量修正一个，inase和一个 kasumi 先试试，两个游戏的骨骼和衣服是否都能适配"。
  全文在 [`docs/magica-cloth-2.md`](docs/magica-cloth-2.md) 第 10 节。
  - 插件解到 `Assets/MagicaCloth2`（不进仓库）；它自己往 `ProjectSettings/ProjectSettings.asset` 加编译符号 `MAGICACLOTH2`，**这个文件不要提交**。F4 里多出 `magica`。
  - a08 的两片长裙和后腰垂片、g04 的前片、左飘带、后片（爆衣早把它们拆成了独立的网格）用 **MeshCloth** 直接模拟网格：顶点按蒙皮权重分固定 / 可动，
    参数从 Magica 自带的裙子预设往"重布料"调（`RoeMagicaCloth.MeshSettings`）。头发、链子、胸和霞的网格布控制点用 BoneCloth。
  - 修了四个坑：胶囊方向（Magica 的胶囊沿轴的负方向伸出，第一版腿等于没碰撞体）、布的静止形状改用绑定姿势、换方案时当场删组件（不然新组件接手旧数据）、
    DOA6 网格是厘米单位缩放 0.01（烘出的皮肤点要用完整矩阵转到世界，之前霞身上一个皮肤点都读不到）。
  - Magica 只在播放模式里跑：`Editor/RoeClothPlayDemo.cs` 进播放模式录像，每步量布陷进身体多少、顿挫、拉伸、离动画多远、翻过去多少（`metrics.txt`）。
  - a08（全段平均）：陷进身体 0.3%（骨骼布料 0.4%，关 2.5%），拉伸 6.6%（7.4%，5.0%），顿挫 6.3 毫米（3.6，0.1）——比骨骼布料"活"，布有褶皱、踢腿时搭在腿上。
    视频 `out\mc2_compare_v2_front.mp4`、`out\mc2_compare_v2_back.mp4`。不是默认方案（`auto` 照旧）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.FitSkirt                                   # 裙子跟腿走的权重 → 人形预制体上的 RoeSkirtRig（在 RoeHelperFit.Run 之后）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtDepth -Graphics                      # 基准姿势插进腿多深：游戏手调 / 钉在骨盆 / 跟腿走
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtSwing -Graphics -Extra '-roeChar','a08','-roeCloth','legacy'   # 某个后端的数字
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.ButtGap -Graphics -Extra '-roeChar','g04','-roeCloths','magica_style'  # 后片离屁股多远 + 特写 → _work\butt
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtRests                                # 两种基准差多少（每个驱动、每节裙骨）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Rests -Graphics -Extra '-roeView','backright'   # 两种基准各录一遍 → _work\rest_demo
python tools\cloth_demo_video.py _work\rest_demo out\skirt_rest_compare.mp4 --variants rest_guard,rest_stance
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtHang -Extra '-roeDrapes','0,1'       # 裙骨偏离竖直多少、离皮肤多远（-roeChains 逐节）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtJumps -Extra '-roeChar','g04'       # 基准姿势一步最多转多少 + 每步耗时
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Rests -Graphics -Extra '-roeRests','rest_stance,drape','-roeView','front'
# 裙子的开关都能在命令行改：-roeDrape 0..1  -roeClearance 0.012  -roeTwist 0..1  -roeFacing 0.2  -roeMeshClearance 0.003；ButtGap 还会报裙子网格陷进皮肤的比例（逐节）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.Skirt -Graphics -Extra '-roeChar','g04','-roePacks','bandai1','-roeCloths','legacy,magica_style'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeCloths','legacy,magica_style','-roeOut','_work\cloth_demo_mc2'
python tools\cloth_demo_video.py _work\cloth_demo_mc2 out\x.mp4 --variants legacy,magica_style --suffix "（Bandai 包）"
# Magica Cloth 2（装了插件才有）：装好没有；爆衣布片的网格、权重；播放模式录像 + 量（-roeNoFrames 只量；magica:字段=值;… 试参数）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMagicaSetup.Check
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMagicaSetup.Pieces -Extra '-roeChar','a08'
.\tools\unity_batch.ps1 -NoQuit -Graphics -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run -Extra '-roeChars','a08,kas011','-roeCloths','magica,magica_style,doa6,off','-roeOut','E:\code\othercode\roe_fighter_unity\_work\mc2_final_front'
python tools\cloth_demo_video.py _work\mc2_final_front out\x.mp4 --chars a08 --variants magica,magica_style,off
```

### 动作包：可插拔的基础动作

基础动作（站架、前进、后退、跑，以及 A–D 四个攻击）做成可以整套替换的**动作包**（`MotionPack`，在 `Generated\motionpacks\`）。
每个包写明每个角色动作用哪个人形片段、每个攻击键出哪一招（击打骨骼、有效帧、攻击距离、伤害、硬直……），以及来源和授权。
游戏自己的技能、受击、倒地不属于动作包，跟着角色走。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.BuildBandai                    # 现在的动捕动作 → bandai1
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','my_pack.json'   # 任何人形片段（或 BVH 动捕）→ 一个包
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/accad_male2.json'   # 开源动捕 → accad_male2
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics -Extra '-roeMotions','my_pack'  # 用哪个包开局
```

- 场景里会放进所有已建好的包；开局用 `-roeMotions` 指定的那个（默认 `bandai1`），游戏里按 **F3** 换下一个包（比赛重新开始，计时下方显示包名）。
- **买来的 Unity 动作怎么接**：把动作包导进工程（比如放在 `Assets/ThirdParty/某包`），写一个 JSON：

  ```json
  {
    "name": "my_pack", "title": "游戏里显示的名字", "source": "Asset Store: 包名", "license": "Asset Store EULA",
    "folder": "Assets/ThirdParty/某包", "humanoid": true,
    "roles": { "guard": "Fight_Idle", "walk": "Walk_Forward", "walk_back": "Walk_Backward", "run": "Run" },
    "strikes": [
      { "button": "A", "name": "jab", "clip": "Punch_Jab" },
      { "button": "B", "name": "hook", "clip": "Punch_Hook", "damage": 60 },
      { "button": "C", "name": "straight", "clip": "Punch_Cross" },
      { "button": "D", "name": "kick", "clip": "Kick_Roundhouse", "speed": 1.2 }
    ]
  }
  ```

  导入时：
  - `humanoid` 为真时，先把那个文件夹里的 FBX 都设成 Humanoid；
  - 片段按名字找（不分大小写，`文件.fbx:片段` 可以指定文件）；
  - 走路类片段复制一份设成循环，并扣掉水平位移（身体由格斗逻辑移动）；
  - 每个攻击放到 a08 身上采样，量出是哪只手或脚、最远够多远、多高、哪几帧在最远距离的 85% 以内（和动捕的招式表同一个算法）；
  - 没写的伤害、硬直按键位给默认值（A 轻、D 重）。

  步速、着地、脚步锁定、辅助骨、布料都是按每个角色身上的实际姿势实时算的，换包不用改别的。
  包里还可以写 `"walkSpeed"`、`"backSpeed"`（米/秒）：这套动作原本走多快就让格斗按多快走，不写就用格斗默认的 1.15 / 0.95。
- 重新导入同名的包时原地更新那个资源，场景里引用的还是它，不用重建场景。

#### 第二个包：开源动捕 `accad_male2`

先调研了十来个公开的动作数据，按"能不能直接下载、授权、有没有格斗动作"挑：

| 来源 | 授权 | 格斗动作 | 结论 |
|---|---|---|---|
| **ACCAD Male 2**（俄亥俄州立大学） | CC BY 3.0，署名即可 | 格斗架势、拳击步法、各种拳、二十来种踢、防御、闪避、挑衅、胜利；每段一个动作 | **用它**：不用登录，一个 7 MB 的压缩包 |
| Quaternius 通用动画库 1、2（免费版） | CC0 | 刺拳、直拳、勾拳、受击、倒地，**没有踢** | 留作备选（FBX，直接走 Humanoid 导入） |
| CMU 动捕库 | 几乎不限用途（不许直接转卖数据） | 空手道（135 号）、拳击、出拳踢腿、起身 | 一段十几到几十秒、120 fps，要自己一招招切；以后补招可以用 |
| 100STYLE | CC BY 4.0 | 只有走跑（各种风格，含面朝前横移） | 不够 |
| LAFAN1（育碧） | CC BY-NC-ND 4.0 | 有对打的长镜头 | 授权不许发布改编后的动作，不用 |
| Bandai Namco 数据集 2 | CC BY-NC 4.0 | 只有走、跑、挥手 | 不够 |
| Mixamo、Asset Store 免费包、Rokoko | 各自条款 | 很多 | 要登录（你的账号），留给你 |

```powershell
python tools\fetch_mocap.py --source accad      # 下载并解压到 _work\mocap\accad\male2（149 段 BVH，30 fps，厘米）
python tools\bvh_preview.py _work\mocap\accad\male2\Male2_E2_JabRight.bvh --every 4 --out sheet.png   # 火柴人连环图，挑帧范围
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/accad_male2.json'
```

这位演员习惯右脚在前（southpaw），站架、步法、防御都是这个架势；每一招左右两边各录了一遍，包里统一用右脚在前的版本：

| 用途 | 段落 | 说明 |
|---|---|---|
| 站架 | `E8_Bounce` | 原地轻跳的拳击架势，切出 0.8 秒一个循环 |
| 前进 / 后退 | `E3_Advance` / `E5_Retreat` | 拳击小碎步（前脚迈、后脚跟），各切出 1 秒一个循环 |
| A 刺拳 | `E2_JabRight` | 前手（右拳） |
| B 前腿回旋踢 | `G11_RoundhouseLeadingRight` | 轻脚 |
| C 直拳 | `E3_CrossLeft` | 后手（左拳），身体前压 |
| D 后腿回旋踢 | `G8_RoundhouseLeft` | 重脚，高踢 |

在 a08 身上量出来：刺拳够到 0.77 米、前腿回旋踢 1.23 米、直拳 0.78 米、后腿回旋踢 1.18 米；播放速度 1.6 / 1.5 / 1.4 / 1.4 倍。

JSON 里的 `bvh` 一块说明怎么把动捕切成片段（节选，`//` 后面是这里加的说明，真正的 JSON 里不能写；完整的见 `tools/motionpacks/accad_male2.json`）：

```json
"bvh": {
  "folder": "_work/mocap/accad/male2",
  "reference": "Male2_A1_Stand.bvh", "restFrame": 0,     // 用哪一帧自然站立建人形骨架（-1 = BVH 的零姿势本身就是 T 姿势时）
  "scale": 0.01,                                          // 米/单位，不写就按腿长判断（厘米、米、英寸）
  "segments": [
    { "name": "jab", "file": "Male2_E2_JabRight.bvh", "from": 14, "to": 50, "limb": "RightHand", "recoverSpeed": 1.5 },
    { "name": "cross", "file": "Male2_E3_CrossLeft.bvh", "from": 10, "to": 58, "limb": "LeftHand", "recoverSpeed": 2, "face": "stance:jab" },
    { "name": "guard", "file": "Male2_E8_Bounce.bvh", "loop": true, "minCycle": 0.4, "maxCycle": 1.4, "face": "stance:jab" },
    { "name": "walk", "file": "Male2_E3_Advance.bvh", "from": 10, "to": 165, "loop": true, "minCycle": 0.8, "maxCycle": 1.4, "face": "travel" }
  ]
}
```

每个段落转成一个人形片段（放在 `Generated\motionpacks\<包名>\bvh\`），后面就和任何人形片段一样：`roles` 和 `strikes` 按段落名引用。
导入器（`RoeMocap`）为此改成了通用的：

- **骨骼名自动识别**：先拆出左右（`Left`/`L`/`_L`/`l` 前缀）和名字主体，按名字找手（`wrist`/`hand`）和脚（`foot`/`ankle`），
  它上面一节是前臂/小腿，再上面是上臂/大腿，再上面同侧的是肩；髋是两条大腿和头的公共祖先；髋到头之间按两臂分叉的位置分成脊柱和脖子，
  和父骨重合（零偏移）的关节跳过。Bandai 的 `Hand_L`、CMU/LAFAN1/Mixamo 的 `LeftHand`、3ds Max 的 `Bip01 L Hand`、ASF 的 `lwrist` 都能认；
  认不出来时可以在 `bones` 里直接写。`tools\bvh_preview.py` 用同一套规则。
- 改完后重跑 Bandai 的转换，生成的片段和招式表**逐字节相同**。
- **对齐方式**（`face`）：
  - 原来的做法是出招按"伸得最远的方向"对准对手，走路按骨盆朝向。格斗架势是侧身站的（骨盆偏开对手约 55°），按骨盆对齐会斜着走，
    所以加了 `travel`（按移动方向）和 `back`（后退）。
  - 每招各按出手方向对齐时，几招的起手姿势互相差到 20° 以上（直拳打出去时身体是拧着的），从站架切过去身体会一扭。
    `stance:jab` 让起手时骨盆和刺拳起手时一样对着对手，出手过程中再平滑转到出手方向，到最远点正好对准，收回时再转回来
    （直拳转 −22°，后腿回旋踢 +14°，前腿回旋踢 −3°）。
- **收招加速**（`recoverSpeed`）：演员收拳很慢，直拳打出去 0.9 秒后才回到架势。最远点之后的部分按这个倍数加速（刺拳 1.5，其余 2）。
- **循环段落**的长度改用秒（`minCycle`、`maxCycle`），`fps` 可以降采样（CMU 那种 120 fps 的数据）。

实测（`RoeFightProbe.FootSlide -roeMotions accad_male2`，着地的接触点每秒滑多少；已含"手臂和腿"一节第 4 条的鞋底摆正）：

| | a08 | g04 |
|---|---|---|
| 前进（0.9 米/秒，动作放 1.92 / 1.40 倍） | 0.20 | 0.14 |
| 后退（0.8 米/秒，1.68 / 1.67 倍） | 0.12 | 0.10 |
| 侧步 | 0.01–0.05 | 0.02–0.03 |

小碎步本来就是后脚拖着地跟上去，所以后退比 Bandai 的正常走路（0.09）滑得多一点；没有净漂移。

演示：`RoeClothDemo.Packs` 让两个角色用同一套按键脚本（站架、前进、后退、两边侧步、A、C、D、B）把每个包各做一遍，
`python tools\cloth_demo_video.py _work\pack_demo out\motion_packs_demo.mp4 --variants bandai1,accad_male2` 左右拼起来，
每格下面写着当前按的键和那个包里这个键是什么招。`RoeFightScene.Record -roeMotions accad_male2` 用新包录一整场电脑对电脑。

坑：

1. **裙子像翅膀一样横着飘。** 第一次导入后，两个角色的裙子在新包的所有动作里都水平伸出去，关掉布料也一样。
   裙子挂在骨盆上，`RoeFightProbe.PelvisCheck` 量出骨盆骨被往前翻了 90°（腿和脊柱由各自的肌肉值摆对了，看不出来）。
   原因在建人形骨架那一步：原来用"髋 → 第一节脊柱"当"上"把骨盆摆正，ACCAD 的第一节脊柱 `ToSpine` 却在髋正后方 8 厘米、和髋一样高，
   摆正后骨盆仰面躺倒了 90°，放到角色身上就是前翻 90°。现在第一节脊柱和"髋 → 脖子"的方向差超过 30° 时改用后者（Bandai 两者只差 1.4°，结果不变）。
2. 后退第一次切到了演员停下来站着的那几帧：站着不动也"首尾很像"。现在 `travel` / `back` 的循环要求每秒至少移动 0.15 米。

#### 第三组：UFE 2 的演示角色（10-04 晚）

用户 10-04 下载了 UFE 2（`Universal Fighting Engine 2 Source v2.7.0a`，Asset Store 的付费包，只在本机用）。
- 它是一套格斗游戏框架（输入、招式、判定、电脑、联机），这里的格斗逻辑已经是自己的，框架没有换。
- 有用的是它四个演示角色的动作：每人一整套格斗动作，还带每招的帧数、出招输入、命中段位。

包不导入工程，只读：

```powershell
python tools\ufe_package.py list "E:\Downloads\Universal Fighting Engine 2 Source v2.7.0a.unitypackage" --grep Characters   # 里面有什么
python tools\ufe_package.py moves "E:\Downloads\Universal Fighting Engine 2 Source v2.7.0a.unitypackage" --out _work\ufe2\ufe_moves.tsv   # 每个演示角色的招式表
# 只把四个角色的动作和模型拷进 Assets/UFE（不入库，GUID 照包里的，以后真导入 UFE 也对得上）
python tools\ufe_package.py extract "E:\Downloads\Universal Fighting Engine 2 Source v2.7.0a.unitypackage" . "^Assets/UFE/Demos/Shared_Assets/Characters/(Robot_Kyle|Ethan|Mecanim_Bot|Mike)/(Animations|Model)/"
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/ufe_kyle.json,tools/motionpacks/ufe_ethan.json,tools/motionpacks/ufe_bot.json,tools/motionpacks/ufe_mike.json'
```

| 演示角色 | 动作 | 用法 |
|---|---|---|
| Robot Kyle（3D 格斗演示的主角） | 59 段人形 .anim，30 fps：站、走、跑、冲刺、站/蹲/跳的轻重拳脚、防御、各种受击、倒地、弹墙、起身、投技、波动、升龙式的发射技 | 动作包 `ufe_kyle`（A 轻拳 ×1.8、B 轻脚 ×1.4、C 重拳、D 重脚，速度照 UFE 的招式表） |
| Ethan（3D 格斗演示） | 36 段人形 .anim，60 fps：两种站架、走、跳、受击、倒地；N1–N3、F2、F3、三段连续技、蹲和跳的攻击、超必杀启动 | 动作包 `ufe_ethan`（A N1 刺拳、B N2 前腿踢、C N3 转身后踢、D F3 冲步飞踢） |
| Mecanim Bot（2D 格斗演示） | FBX（人形，用 femalerobot 的骨架）+ .anim：轻中重拳脚、百裂踢（houyoku_sen）、受击、倒地、起身 | 动作包 `ufe_bot`；Fiona 的倒地、躺地、超必杀 |
| Mike（2D 格斗演示） | 43 段**旧版（Legacy）**动画，挂在 3ds Max Biped 上：波动拳、升龙拳、开场、胜利、各种拳脚 | 先转成人形（下面），和 Kyle 是同一套动作数据（量出来时间完全一样），所以不放进 F3，只给 Fiona 用它的开场、胜利、波动拳、升龙拳 |

- 四个键照拳皇：A 轻拳、B 轻脚、C 重拳、D 重脚（UFE 的 Button1 / Button4 / Button2 / Button5）。
- 招式表（`_work\ufe2\ufe_moves.tsv`）是从包里的 MoveSet / MoveInfo 资源直接读的：每招用哪段动作、总帧数 / 发生 / 持续 / 收招、出招输入（↓↘→+拳之类）、
  每一下的命中帧、段位（中 / 下 / 上 / 浮空 / 击倒……）、强弱、伤害、硬直。现在只用了它的播放速度，伤害和硬直还按这里的键位默认值。
- 导入器（`RoeMotionPacks`）加的三样：
  - 角色写成 `-片段名` 表示倒着放：UFE 的后退就是把前进倒着放（Kyle、Mike 都这样）；
  - `rig` 指定一个模型时，文件夹里不是人形的片段（Generic、Legacy）先在这个模型上采样、按它自动建的人形骨架读出肌肉值，转成人形片段（`ConvertGeneric`）——Mike 就是这样转的；
  - 片段可以直接写资源路径（`Assets/.../x.anim`、`Assets/.../x.fbx:片段`），`-roeSpec` 可以一次给几个 JSON。
- 坑：`RoeMotionPacks.Sheet` 一次编辑器更新里连拍好几张时，蒙皮只算第一次，每张都是第一帧的姿势；现在拍之前打开 `forceMatrixRecalculationPerRender`。

对比视频 `out\ufe_packs_demo.mp4`：Fiona 和 a08 用同一套按键，左起 Bandai（原来的默认）、Robot Kyle、Ethan、Mecanim Bot。

#### 全部站立普通攻击放到 Inase 身上（10-05）

用户问："普通攻击可以用在 inase 的身上看看效果"。上面三个包每个只占 A–D 四个键，这里把 UFE 演示角色的站立普通攻击全放到 a08 身上，供挑选：

- 候选包 `tools/motionpacks/ufe_normals.json`，20 招：
  - Robot Kyle 6 招：轻拳、轻脚、重拳、重脚，加两个"前 + 键"的指令普通技（踹飞、后空翻踢）；
  - Ethan 6 招：N1、N2、N2 追加、N3、↘ + 中（挑空）、→ + 重；
  - Mecanim Bot 6 招：轻、中、重拳和轻、中、重脚；
  - Mike 2 招：中拳、中脚（他的轻、重攻击和 Kyle 是同一份动作数据）。
- 没放进来的：
  - 蹲着和跳起来的普通攻击、投技、必杀技：格斗逻辑没有蹲、跳、投；
  - Ellen：她是 2D 精灵动画，没有骨骼动作。
- 每招的速度、段位、浮空都照 UFE 的招式表。
- "能打中的时间"也照 UFE 的帧数据，写在 JSON 的 `hit` 里：
  - UFE 的帧是每秒 60 帧、已经算上播放速度的，所以第 f 帧 = 片段的 f × 速度 / 60 秒；
  - 原来按"手脚往前伸到最远的 85%"去量，碰到往上踢的招就错了：Kyle 的后空翻踢量到的是起跳前的那一下；
  - 有了 `hit`，导入器（`RoeMotionPacks.Measure`）只在这段时间里找伸得最远的手或脚。
  - Mecanim Bot 的轻脚例外：UFE 表里的帧数和动作长度对不上，数据本身就这样。
- JSON 里的 `who` / `zh` / `ufe` / `ufeFrames` 是给人和视频看的备注，导入器不读。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/ufe_normals.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Sheet -Graphics -Extra '-roePack','ufe_normals','-roeChars','a08'
python tools\strike_sheet.py out\motion_sheets\ufe_normals out\a08_ufe_moves_sheet.jpg --pack tools\motionpacks\ufe_normals.json --tile 180
# 逐招录像：真实的格斗逻辑里，她用场景当前动作包的站架，这一招临时放在 A 键上，按一次
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','ufe_normals','-roeChars','a08'
python tools\strike_demo_video.py _work\strike_takes out\a08_ufe_normals.mp4
```

- 检查图 `out\a08_ufe_moves_sheet.jpg`：每招一行 8 帧，左边写着谁的招、用哪只手或脚、够多远、什么时候能打中。
- 视频 `out\a08_ufe_normals.mp4`（72 秒）：
  - 每招先按实际速度放一遍，再半速慢放一遍；
  - 能打中的那几帧右下角亮红色"能打中"。
- `StrikeTakes` 每一帧都拍（每秒 60 帧，慢放才不卡），镜头比别的演示远一点（4.6 米），高踢的脚不出画。
- 挑中的招可以做成她自己的招式（`strikesOnly` 的包，像 g04 的不知火舞）。

#### Inase 用 Mecanim Bot 的普通攻击（10-05）

用户看完上面的视频说："inase用mecanim bot的动作"。

- 她自己的招式包 `tools/motionpacks/ufe_bot_inase.json`（`strikesOnly`，像 g04 的不知火舞），建场景时默认给 a08（`-roeOwnStrikes` 的默认值加了 `a08=ufe_bot_inase`）。10-06 起默认换成拿巨剑的四招（下一节），这套还在，换回的写法见下一节。
- Bot 有 6 个站立普通攻击，按键只有 4 个：
  - A 轻拳：刺拳，0.17 秒，够到 0.55 米；
  - B 中脚：中段前踢，0.80 米；
  - C 重拳：冲步直拳，0.98 米；
  - D 重脚：回旋高踢，1.23 米。
  - 没用的：轻脚（UFE 表里的帧数和动作对不上，只有 0.125 秒、够到 0.39 米）、中拳（上勾拳）。换招改这个 JSON。
- 速度和"能打中的时间"照 UFE 的招式表（从 `ufe_normals.json` 抄过来）；UFE 没给这几招击倒，这里也没有。
- 站架和走路还是当前动作包的（F3），和挑招的视频里一样。
- 视频：`out\a08_bot_strikes.mp4`（四招，每招先实速再半速）；电脑对电脑 `out\fight_cpu_match_a08_eve09.mp4`（Inase 对 Eve，120 秒）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/ufe_bot_inase.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','ufe_bot_inase','-roeChars','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_bot'
python tools\strike_demo_video.py _work\strike_takes_bot out\a08_bot_strikes.mp4 --pack tools/motionpacks/ufe_bot_inase.json --id a08
```

#### Inase 拿巨剑的普通攻击（10-06）

用户问："动作有拿巨剑的普通攻击么，给inase拿着长剑的找一些普通攻击，可以看roe中是否有现成的，或者Universal Fighting Engine 2 Source v2.7.0a 里是否有"。

**现成的有没有**：两边都没有。

- ROE 没有普通攻击：每套服装只有站架、三个技能、受击、倒地、躺地（`idle_01/02`、`react_01/02`、`skill_01..03`、`hurt`、`die`、`rip`）。
- UFE 2 的 3D 演示角色（Kyle、Ethan、Mecanim Bot、Mike）都是空手；Ellen 是 2D 精灵图，没有骨骼。
- 能用的：Vindictus 里 Fiona 的剑招（单手长剑，右手），加上 Inase 自己技能里的挥砍。

**她的剑**：其实是两把巨剑（各 1.59 米）。

- `wp_a08_l` 的骨骼是 `Point007_L`，带一条 4 节的布料链；`wp_a08_r` 的骨骼是 `Point007_R`。两根都挂在 `Root` 下。
- 站架 `idle_01` 里两把重合成一把：剑尖拄地，右手握着剑柄末端。
- 技能 1 第 58–111 帧：`Point007_L` 在右手里，是砍的握法（剑身从拇指一侧伸出）；`Point007_R` 在左手里。
- 人形模型里这两根骨骼挂在骨盆上，所以以前动捕动作里剑留在原地，只在她游戏自己的动作（技能、开场、胜利）里显示。

**握剑**：`RoeGrips`（运行时组件）每帧把剑的骨骼放进手里：位置 = 手 + 手的旋转 × 偏移，在 `ueRig.DriveTwists` 之后、布料之前。

- 握法从游戏自己的动作数据里量：`tools/roe_grip.py` 读 ripper_tpose 解码的技能 1（第 58–111 帧），剑在右手里最多晃 7.6 厘米 / 3°（剑的骨骼原点在剑尖，离手 1.25 米）。结果在 `tools/roe/a08_grip.json`。
- 为什么不在 Unity 里量：在导入的骨架上采样同一段站架，右手的朝向和游戏里差几度，到剑尖就差 15 厘米。
- 两把剑当一把用：`--pair Point007_R=Point007_L --pair-clip <idle_01.json>`，第二把按站架里两把的相对位置跟着第一把。
- `RoeWeaponGrip.Build` 把握法和剑身（`RoeBlade`：右手，剑身 1.28 米）装到 `a08_fighter.prefab` 上。检查图 `out\weapon_grip\a08_0..2.png`：站架、握上、抬手。
- 重建 a08 的人形预制体（`ConvertAll` 不带 `-roeKeepPrefab 1`）时会自动重新装。建场景时如果招式要握剑、预制体上却没有握法，会出警告。
- 只有招式包要握剑时（`MotionPack.grip`）才握着：这时剑一直显示，招式按剑身判定。换回别的招式包，剑还和以前一样只在技能里出现。

**挑招**：13 个候选（`tools/motionpacks/a08_sword_candidates.json`）。

- 候选：Fiona 的连击 1–4、重击 1–4、冲刺重击、格挡反击、霸体反击、终结技，加上 Inase 技能 1 的大挥砍（2.1–3.4 秒）。
- 都放到 a08 身上，握着她的巨剑，在真实的对战逻辑里拍。
- 视频 `out\a08_sword_candidates.mp4`：每招先按实际速度放，再半速慢放。
- 总览图 `out\a08_sword_candidates_sheet.jpg`（`tools/takes_sheet.py`）：按键后每 0.1 秒一格，红条是能打中的时间。
- 检查图 `out\a08_sword_candidates_moves_sheet.jpg`（`RoeMotionPacks.Sheet` + `tools/strike_sheet.py`）：在影棚里每招从头到尾 8 格，握法照样生效。整段放时最后几格是 Vindictus 收招回站姿，所以定下的招要裁短。

**默认四招**：`tools/motionpacks/a08_sword.json`（`strikesOnly` + `grip`）。时间都是按键之后、按招式的速度算的。

| 键 | 招式 | 出处 | 出手 | 够到 |
|---|---|---|---|---|
| A | 斜劈 | Vindictus 连击 2（Attack02） | 0.25 秒 | 2.38 米 |
| B | 低位横扫 | Vindictus 连击 3（Attack03） | 0.25 秒 | 2.26 米 |
| C | 举剑砸地 | Vindictus 连击 4（Attack04） | 0.44 秒 | 2.73 米 |
| D | 大回旋斩（击倒） | Inase 技能 1 | 0.60 秒 | 2.31 米 |

- 挑法：
  - 不用 Fiona 自己的四招（连击 1、重击 3、格挡反击、重击 4），两人对打时动作不重样；
  - 格挡反击、霸体反击会跳出原地；
  - 冲刺重击几乎不挥剑；
  - 终结技 0.9 秒才出手。
- 每招裁到挥完、停住的地方（`to`）；Vindictus 的招后面还有 2 秒收招回站姿。
- 能打中的时间（`hit`）按剑尖定：
  - 导入时加 `-roeTips <目录>`，每招写出剑尖每帧在身前多远、多高、多快（`<招式名>_tip.tsv`）；
  - 取剑尖扫过身前的那几帧。
  - 自动量的不准：1.3 米的剑，手臂随便一动剑尖就超过 5 m/s，从头到尾都算"在砍"。
- 四招都是中段。C、D 是手动设的（`level`）：自动量取剑尖最远那一帧的高度，砸地那一下剑落在地上会判成下段，回旋斩会判成上段，其实都该站着防。
- 换招：改这个 JSON（候选里的任何一招，或别的人形动作），重新导入、重建场景。
- 视频：`out\a08_sword_strikes.mp4`（四招，每招先实速再半速）；电脑对电脑 `out\fight_cpu_match_a08_sword.mp4`（Fiona 对 Inase，120 秒：第一回合时间到 Fiona 赢，第二回合 Inase 把 Fiona KO）。
- 图：总览图 `out\a08_sword_moves_sheet.jpg`（对战里拍的，按键后每 0.1 秒一格）；检查图 `out\a08_sword_check_sheet.jpg`（影棚里每招 10 格）。
- 爆衣演示：拿巨剑的 Inase 打 Luf（g04），`out\clothes_burst_demo_g04_by_a08_sword.mp4`（38 秒，用户 10-06："inase 把luf 再打爆衣再实验一下"）。
  - 流程和"爆衣"一节的演示一样，打两局：每局先走近放超必杀，然后电脑接着打到 KO。
  - 四段全掉：第一局超必杀打掉前片、左飘带、后片和下摆毛边、裙饰和流苏，KO 打掉主裙；第二局超必杀打掉手套、上衣、项圈和腰带，决胜的 KO 打掉内裤。
  - 第一局的 KO 是技能 2 打的；第二局决胜的 KO 是巨剑普通攻击砍的。
- 建场景时默认给 a08 这套（`-roeOwnStrikes` 的默认值改成 `a08=a08_sword`）。参数会替换整张表，换回 Mecanim Bot 的拳脚要写全：`-roeOwnStrikes g04=doa6_mai,g05=doa6_mai,a08=ufe_bot_inase`。

```powershell
python tools\roe_grip.py E:\game_export\RiseOfEros\Inase\vmd\pc_a08_hd\_clips\skill_01.json a08 --frames 58-111 --slack 0.08 --turn 4 --pair Point007_R=Point007_L --pair-clip E:\game_export\RiseOfEros\Inase\vmd\pc_a08_hd\_clips\idle_01.json
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeWeaponGrip.Build -Graphics -Extra '-roeChars','a08'
# 候选：导入、拍、视频、总览图
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/a08_sword_candidates.json','-roeTips','E:\code\othercode\roe_fighter_unity\_work\sword_tips'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','a08_sword_candidates','-roeChars','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_sword'
python tools\strike_demo_video.py _work\strike_takes_sword out\a08_sword_candidates.mp4 --pack tools/motionpacks/a08_sword_candidates.json --id a08
python tools\takes_sheet.py _work\strike_takes_sword out\a08_sword_candidates_sheet.jpg --pack tools\motionpacks\a08_sword_candidates.json
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Sheet -Graphics -Extra '-roePack','a08_sword_candidates','-roeChars','a08'
python tools\strike_sheet.py out\motion_sheets\a08_sword_candidates out\a08_sword_candidates_moves_sheet.jpg --pack tools\motionpacks\a08_sword_candidates.json
# 定下的四招
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/a08_sword.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','a08_sword','-roeChars','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_a08_sword'
python tools\strike_demo_video.py _work\strike_takes_a08_sword out\a08_sword_strikes.mp4 --pack tools/motionpacks/a08_sword.json --id a08
python tools\takes_sheet.py _work\strike_takes_a08_sword out\a08_sword_moves_sheet.jpg --pack tools\motionpacks\a08_sword.json --cols 12 --tile 200
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Sheet -Graphics -Extra '-roePack','a08_sword','-roeChars','a08','-roeShots','10'
python tools\strike_sheet.py out\motion_sheets\a08_sword out\a08_sword_check_sheet.jpg --pack tools\motionpacks\a08_sword.json --cols 10
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','fio005','-roeP2','a08','-roeSeconds','120','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_a08_sword'
python tools\make_video.py _work\fight_a08_sword out\fight_cpu_match_a08_sword.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','g04','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_g04_a08sword'
python tools\burst_video.py _work\burst_demo_g04_a08sword out\clothes_burst_demo_g04_by_a08_sword.mp4
```

#### 卡地亚（Kart，b10）拿斧头打普通攻击（10-06）

用户说："把ROE中的卡地亚也加入战斗，一个拿斧子和枪的上衣是红色的卡地亚"。

**卡地亚就是 Kart 的 b10**：红色短夹克、拿斧头、大腿上挂两把枪的那一套。她 10-03 晚就已经在战斗里了，选人界面上写的是 KART。

- 以前她打普通攻击用的是动作包的拳脚，斧头和枪只在技能里出现。
- 现在照 Inase 巨剑的做法：平时斧头握在手里，四个普通攻击都用斧头。

**她游戏里武器是怎么拿的**（`tools/roe_clips.py` 把她的游戏动作解码出来再量）：

- 站架 `idle_01`：小斧头握在右手。两把枪插在大腿的枪套里，跟着大腿走，不在手上。
- 技能 1：冲上去把斧头扔出去。技能 2：先把斧头扔在地上，两把枪自己从枪套里飞出来，悬在空中开火。技能 3：空中翻滚、扔斧头，枪也在空中开火。
- 她的手从来不拿枪。所以普通攻击只用斧头，枪还是在技能里飞出来开火，和游戏里一样。

**握法**（`tools/roe/b10_grip.json`）：

| 武器（骨头） | 挂在哪 | 在游戏站架里晃动多少 |
|---|---|---|
| 斧头 `Axe_01` | 右手 | 0.46 厘米 / 0.6° |
| 右手枪 `Pistol_R` | 右大腿（枪套） | 0.03 厘米 / 0.07° |
| 左手枪 `Pistol_L` | 左大腿（枪套） | 0.12 厘米 / 0.07° |

- 枪套在爆衣第 1 段就掉了，两把枪跟着消失（`--burst Pistol_R=1`，`RoeGrips.Grip.burstStage`）。技能里枪照样从空中飞出来。
- 斧头的判定线从手到斧头端 0.43 米（斧头全长 0.60 米）。检查图 `out\weapon_grip\b10_0..2.png`：游戏原样、套上握法、换一个手臂姿势。
- 为这次通用化的改动：
  - `roe_grip.py` 加了 `--bones`（量哪几根骨头）、`--holders`（除了手还能挂在哪，比如大腿）、`--burst`（爆衣第几段掉了就消失）；
  - `RoeWeaponGrip` 的握点可以是任意人形骨头；
  - `FighterRig.ShowWeapons` 握着武器时只显示握住的那几件，每一步按爆衣进度重新判断。

**挑招**：14 个候选（`tools/motionpacks/b10_axe_candidates.json`）。

- 候选：Vindictus 的 12 个单手剑招（小斧头和单手剑挥法一样），加上她自己技能里扔斧头的两段（斧头这里一直握在手里，扔变成了挥）。
- 视频 `out\b10_axe_candidates.mp4`，总览图 `out\b10_axe_candidates_sheet.jpg`。

**默认四招**：`tools/motionpacks/b10_axe.json`（`strikesOnly` + `grip`）。

| 键 | 招式 | 出处 | 出手 |
|---|---|---|---|
| A | 横扫 | 她自己技能 1 的那一下（0.3–1.0 秒） | 0.17 秒 |
| B | 劈砍 | Vindictus 重击 1 | 0.13 秒 |
| C | 冲步斩 | Vindictus 重击 2 | 0.33 秒 |
| D | 举斧砸地（击倒） | Vindictus 连击 4 | 0.44 秒 |

- 挑法：
  - A、B、C 三招 Fiona 和 Inase 都没用；D 和 Inase 的 C 是同一个动作。
  - 终结技剑尖几乎够不到身前；技能 3 开头那段是冲步加跪地，看不出在打；冲刺重击几乎不挥斧头。
- 四招都手动设成中段：自动量取斧头最远那一帧的高度，A 会判成上段、B 判成下段。
- 建场景时默认给 b10 这套（`-roeOwnStrikes` 的默认值加了 `b10=b10_axe`）；换回拳脚就在表里去掉这一项。

**膝盖和小腿**（用户问："卡地亚的膝盖和小腿是不是有问题"）：有问题，已修。对比图 `out\b10_knee_fix.jpg`。

- **样子**：
  - 冲步、劈砍这类深弓步里，往后伸的那条腿，膝盖到小腿被扯成一条细长的带子，连到靴口；
  - 往前弯的那条腿，膝盖像被切掉一截；
  - 左边的枪套悬在半空。
  - 以前用拳脚时腿弯得浅，不明显。
- **原因**：
  - b10 的大腿扭转骨 `Bip001 L/RThighTwist`、`ThighTwist1` 挂在脊柱下面，不在腿下面。
  - 拟合辅助骨（`RoeHelperFit`）只收"最近的人形祖先是四肢"的骨，把这 4 根漏了。动捕动作里它们一直停在站架的位置。
  - 膝盖一圈的皮肤（衣服下面换上的底模身体）每条腿约 116 个顶点主要挂在 `ThighTwist1` 上。大腿往后一摆，这块皮留在原地，靴子跟着小腿走，中间就被拉长了。
  - 大腿的绑带和枪套也挂在这几根骨上。
- **修法**：
  - 名字带 `ThighTwist` / `CalfTwist` 的骨，不管挂在哪，都当腿的辅助骨拟合。
  - b10 现在拟合 24 根（原来 20 根）。这 4 根都跟着大腿走，误差 2.4–5.1°。
  - a08、g04、g05 的腿部扭转骨本来就挂在大腿下，不受影响。
- **检查工具**：
  - 新加 `RoeFightProbe.LegShots`：按对战的驱动方式把她摆到某招某一刻，近距离分"全部 / 只看身体 / 只看衣服"三层、从右、前、左三个方向拍腿。
  - 日志里列出膝盖、小腿一圈的皮肤主要挂在哪根骨上（就是靠它找到的原因）。
  - `RoeMotionPacks.Sheet` 的检查图原来不跑辅助骨，现在跑了，和对战里看到的一样。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.Run -Extra '-roeChars','b10'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.LegShots -Graphics -Extra '-roeChar','b10','-roeClip','ax_lunge','-roeTimes','0.45','-roeSpeed','1.1'   # → out\leg_shots
```

**大腿根**（用户说："大腿根的权重还是不对呀，看看convert_TO_MMD5 里的大腿和臀部的部分是怎么修复的"）：查清了，主要不是权重，是这几招把腿摆得太拧。

- 对比图：`out\b10_thigh_root.jpg`（之前 / 现在）、`out\b10_thigh_root_settings.jpg`（三档强度）。
- 对比视频：`out\b10_axe_strikes_compare.mp4`，左边改之前，右边改之后。
- **样子**：膝盖修好以后，斧头冲步、劈砍、砸地里，弯着的那条腿那边的屁股像往下坠到大腿上，鼓出一块。修膝盖以前，大腿扭转骨不跟大腿走，屁股是圆的，但膝盖坏了。
- **原因**：
  - 这几招是别人的动作重定向到她身上的。着地那条腿弯着的时候，大腿往里转了 45–60°：`ax_lunge` −45°、`ax_chop` −60°、`ax_slam` −52°。她游戏自己的 10 个动作里最多 36°。
  - 大腿往里一转，小腿就往外撇，整条腿成了 W 形，屁股和大腿根的轮廓看上去就像坠下来一块。
  - 证据：同一帧里大腿的朝向和自转都不变、只把小腿摆回正下方，屁股那一片的像素和原来完全一样（差值图全黑），变的只是腿的样子。
  - 在她游戏自己的动作里（包括腿抬得最高、分得最开的帧），现在的骨骼和权重摆出来的样子跟游戏关键帧一样，没有鼓包。
- **试过、没用的**（检查工具都留着，图在 `out\hip_try`、`out\hip_look`）：
  1. ROE 导 PMX 时（Convert_to_MMD5 之前那一步）修大腿根的办法：大腿扭转骨按它那块皮在大腿上的位置取跟随比例（0.5 + t/0.7），b10 算出 0.86/0.87。
     - 弓步里几乎没变化。
     - 在游戏自己的抬腿动作里反而更差：这根骨会跟着大腿自转 86%，而游戏里它是不跟自转的。skill_03 第 1.10 秒，游戏的这根骨离"只跟摆动、不跟自转"差 4°，离"整根跟着转"差 55°。
     - 已撤回。
  2. 同一个规则改成逐顶点：大腿根一圈的权重按位置从骨盆渐变到大腿。
  3. 髋关节加"半角辅助骨"（转角取骨盆和大腿的一半），减轻线性蒙皮转 90° 时的塌陷。
  4. 屁股上挂在大腿的权重挪回骨盆。
  5. 换回游戏底模（b01）自己的权重，并像 PMX 导出那样驱动底模自带的臀部辅助骨：Muscle Strand 在臀部取 0.67、髋外侧取 0.50，ThighTwist_LT 取 0.85。

  这几种都只是把鼓包挪个位置，或者多出一道折痕。
- **修法**：
  - 大腿扭转骨回到用她游戏动作拟合的驱动：跟着大腿摆动，自转只跟左 1/3、右一半。
  - 拟合时不再把膝盖的"自转"当来源。膝盖是铰链，那一项在游戏动作里是噪声（原来拟合出 1.5 倍），动捕里还会被放大。
  - 新加 `FighterRig.LimitRoll`：动捕动作里，脚着地的那条腿，大腿相对骨盆往里或往外转超过 15° 的部分只保留 30%，小腿和脚跟着腿走，不再往外撇。踢腿时抬起的腿不受影响。
  - 卡地亚默认打开：建场景时 `-roeLegRoll` 的默认值是 `b10=15:0.3:0`。三个数依次是：
    - 从多少度开始限制；
    - 超出的部分保留多少；
    - 脚怎么放：0 跟着腿走，1 保持原来的高度，2 留在原地。
  - 别的角色不变；要给谁加，就往表里加一项。
  - 脚放法选 0 的原因：选 2（脚留在原地）时，膝盖会被拉回原来的位置，W 形又回来了。
  - 更强的一档 `b10=5:0:0`：左小腿竖直往下，W 形没了，代价是弓步窄一些（见 `out\b10_thigh_root_settings.jpg`）。
- **检查工具**（`RoeFightProbe`）：
  - `HipScan`：每个动作里大腿的最大摆动、最大外展、自转范围。
  - `HipTry`：同一刻按多种办法摆出来对比，`-roeTry` 列出办法，`-roeLegRoll` 临时改限制。
  - `HipLook`：按权重上色；把底模用它自己的权重摆成同一个姿势；画出两者逐顶点的差。
  - `HipWeights`：大腿根前后一圈挂在哪些骨上。
  - `BodyDump`：顶点、权重、关节导出到 `_work\hip_dump`。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.HipScan -Graphics -Extra '-roeChar','b10'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.HipTry -Graphics -Extra '-roeChar','b10','-roeClip','ax_lunge','-roeTimes','0.45','-roeSpeed','1.1','-roeTry','strong','-roeLegRoll','5:0:0'   # → out\hip_try
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics -Extra '-roeLegRoll','b10=5:0:0'   # 换成更强的一档
```
- 视频 `out\b10_axe_strikes.mp4`（四招，每招先实速再半速）；总览图 `out\b10_axe_moves_sheet.jpg`；检查图 `out\b10_axe_check_sheet.jpg`；电脑对电脑 `out\fight_cpu_match_b10_axe.mp4`（Kart 对 Inase，120 秒）。
- 爆衣演示（Inase 打 Kart）`out\clothes_burst_demo_b10_axe.mp4`：第 1 段打掉枪套时，两把枪一起消失；她游戏自带的动作（技能、回合结束）里枪照游戏的样子出现。

```powershell
python tools\roe_clips.py b10                                   # 她的游戏动作 → _work\roe_clips\b10
python tools\roe_grip.py _work\roe_clips\b10\idle_01.json b10 --bones Axe_01,Pistol_R,Pistol_L --holders "Bip001 R Hand,Bip001 L Hand,Bip001 R Thigh,Bip001 L Thigh" --slack 0.02 --turn 3 --burst Pistol_R=1 --burst Pistol_L=1
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeWeaponGrip.Build -Graphics -Extra '-roeChars','b10'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/b10_axe_candidates.json','-roeTips','E:\code\othercode\roe_fighter_unity\_work\axe_tips'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/b10_axe.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','b10_axe_candidates','-roeChars','b10','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_axe'
python tools\strike_demo_video.py _work\strike_takes_axe out\b10_axe_candidates.mp4 --pack tools/motionpacks/b10_axe_candidates.json --id b10
python tools\takes_sheet.py _work\strike_takes_axe out\b10_axe_candidates_sheet.jpg --pack tools\motionpacks\b10_axe_candidates.json
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','b10_axe','-roeChars','b10','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_b10_axe'
python tools\strike_demo_video.py _work\strike_takes_b10_axe out\b10_axe_strikes.mp4 --pack tools/motionpacks/b10_axe.json --id b10
python tools\takes_sheet.py _work\strike_takes_b10_axe out\b10_axe_moves_sheet.jpg --pack tools\motionpacks\b10_axe.json --cols 12 --tile 200
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Sheet -Graphics -Extra '-roePack','b10_axe','-roeChars','b10','-roeShots','10'
python tools\strike_sheet.py out\motion_sheets\b10_axe out\b10_axe_check_sheet.jpg --pack tools\motionpacks\b10_axe.json --cols 10
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','b10','-roeP2','a08','-roeSeconds','120','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_b10_axe'
python tools\make_video.py _work\fight_b10_axe out\fight_cpu_match_b10_axe.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','b10','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_b10_axe'
python tools\burst_video.py _work\burst_demo_b10_axe out\clothes_burst_demo_b10_axe.mp4
```

### 爆衣：衣服按件掉，掉完是全身（10-03）

调研和整个方案在 [`docs/clothes-burst.md`](docs/clothes-burst.md)。规则是用户 10-03 定的（"按建议"），下午又加了一条：

- **什么时候掉**：超必杀的最后一下打中谁，谁掉下一段；谁被 KO，谁也掉下一段。
- **决胜的 KO 全掉**：决定整场胜负的那次 KO，把输家剩下的全部打掉。这条是下午加的，用户说"让爆衣都爆掉"；开关是 `FightGame.burstAllOnFinalKO`，默认开。
- **掉了就不回来**：回合之间也不恢复，新开一场（或者按 F3 / F4 重开）才穿回去。
- **怎么掉**：护甲飞出去，布料落下来，按固定顺序掉（不看打在哪里）。烧边以后再做。
- **开关**：F6（关掉时立刻全部穿回去，屏幕上方显示 `CLOTHES BURST: on / off`）。`ROEFighter.exe -roeBurst 0` 以关闭状态启动；录像也认 `-roeBurst 0/1`。

分两步做的：

1. 上午：只掉下面本来有完整皮肤的外层（游戏把衣服盖住的皮肤删了，见 `docs/clothes-burst.md` 第 2、6 节）。
2. 下午：用户说"做个 inase 结合 nude 的模型，让爆衣都爆掉"。我把家族的裸体底模接到套装骨架上，代替套装的皮肤（下面"底模身体"），之后贴身的护甲、胸甲、上衣、手套、内裤也都能掉。

两人掉什么（规则表 `Assets\RoeFighter\Editor\Burst\a08.json`、`g04.json`）：

| | 第一段 | 第二段 | 第三段 | 第四段 | 一直留着 |
|---|---|---|---|---|---|
| a08 Inase | 长裙片（左右）、胯甲、裙链、后腰垂片、腰侧细带：9 块，11,560 个三角形 | 左肩甲和肩链、项圈、右上臂链：3 块，4,541 个 | 护手（连左小臂的链环）、腕上流苏、护胫、凉鞋：11 块，6,743 个 | 胸甲、护裆：3 块，1,546 个 | 头冠、耳饰、戒指 |
| g04 Luf | 前片、左飘带、后片和下摆毛边、裙饰和流苏：5 块，7,086 个 | 主裙（前后两大片和腰上一圈）：2,636 个 | 上衣连项圈和腰带、手套：3 块，11,760 个 | 内裤：1,104 个 | 鞋、头饰 |

g04 的鞋一直穿着：她的脚和底模的高跟角度不同（差 1–1.4 厘米），鞋里那截底模删掉了，免得穿出来。

检查图 `out\clothes_burst\unity\a08_sheet.png`、`g04_sheet.png`：
- 第一行是站姿下的原样、第一段后……第四段后，正反两面；
- 第二行是分组上色。
- 掉完四段，正反两面都没看到洞或接缝（脖子、手腕、脚踝放大看过；只看了这些角度，没有逐点量）。

演示 `out\clothes_burst_demo.mp4`：
- 每人一段，打两局：每局对手先走近放超必杀，挨打的一方剩 1 点血后由电脑接着打到 KO；
- 第二局的 KO 决定胜负，剩下的全部掉光；
- 左边近景跟着挨打的一方，右边是比赛镜头，下面写着正在发生什么。

怎么做的：

- **编辑器里拆开**（`Editor\RoeBurstBuilder.cs`）：
  - 读规则表里列出的服装渲染器，按共用的顶点位置（0.1 mm 内焊接）把三角形分成连通的块。
  - 每块按"权重最大的骨头"等条件归组。同一组、同一侧的块合成一个新的蒙皮渲染器；左 / 中 / 右按绑定姿势里骨盆和两条大腿的位置算。
  - 新渲染器的骨头、材质、包围盒、阴影设置都照抄原来的；原渲染器换成去掉这些块的网格。
  - 网格存在 `Generated\<id>\burst\`，重建时原地改写，场景里的引用不会断。
  - 建比赛场景时（`RoeFightScene.MakeFighter`）每个角色拆一遍，并挂上 `RoeClothesBurst`。没有规则表的角色不拆，什么也不变。
  - 拆完、爆之前和原来一模一样：同一个姿势、同一台相机，拆前拆后逐像素比较，最大差 2/255（后期的抖动噪点），差值超过 2/255 的像素为 0。
    这是第一步时量的；第二步换上底模身体后，皮肤本身换了贴图，正面约 5–7% 的像素不一样（主要是皮肤的细节纹理）。
- **底模身体**（`Editor\RoeNudeBody.cs`，第二步）：
  - **用的是谁**：家族的裸体底模，a08 用 a01，g04 用 g01。这是游戏 H 场景用的身体，在 `chara_bare_pc_<家族>01_nk*` 包里，只导了身体网格、材质和贴图到 `Assets\ROE\a01`、`g01`。
    底模网格里还有头、眼睛、睫毛几个子网格，只用身体那个（0 号）。
  - **整块替换**：它和套装的皮肤几乎重合，所以整块代替套装皮肤（a08 `body1` 的 0 号子网格，g04 `body` 的 1 号），没有接缝。
    g04 脖子那一圈（0 号子网格）底模盖不住，留着。
  - **骨头不能按名字绑**：底模自己的骨架比套装多几十根骨头。做法是"权重转移"，每个顶点照抄贴着它的东西的骨骼权重：
    - 1.5 厘米内有套装皮肤，就抄皮肤的；
    - 皮肤被删掉的地方，抄贴身的衣服块，比如胸甲、护胫、手套。"贴身"按几何判断：一块衣服的顶点到底模身体的中位距离不超过 2.5 厘米；裙片、链子、飘带离身体远，不算。
    - 取附近最多 8 个点按距离加权，留权重最大的 4 根骨头。
    - 这样身体动起来和它代替的皮肤、护甲完全一样。
  - **永远不掉的部件下面**：离这种部件 2.5 厘米内、又不贴着套装皮肤的三角形删掉（g04 的鞋里删了 5,759 个三角形），免得穿出来。
  - **结果**：
    - a08：19,880 个顶点，17,760 个抄套装皮肤（最近顶点距离中位 1.5 毫米），2,120 个抄护甲；
    - g04：19,751 个顶点，11,294 个抄皮肤（中位 3.9 毫米），8,457 个抄衣服——她的上衣、手套、内裤下面删掉的皮肤多，体形也和底模差得多一些。
  - **规则表里的写法**：`"nude"` 一段写底模的预制体、身体渲染器、子网格、材质，要接到的套装渲染器（`attachTo`），以及它代替的皮肤（`replace`）；
    `fill` 写从底模的哪个子网格补套装没有的部分（`missingFrom`：拿来比对的套装渲染器），b10、g05 用它补领子下面的脖子（见"新角色：b10 和 g05"）；
    `ownWeights`（骨头名的正则）：底模挂在这些骨头上、套装皮肤又够不着的顶点，不抄盖在上面的衣服，用底模自己的权重，b14 的右上臂用它（见"Kart 最新的一套（b14，鹤袍）加入战斗"）。
  - **顺手修了一个老问题：皮肤 LUT 一直缺着**。
    - 套装的皮肤和脸的材质都引用游戏的皮肤散射查找表 `pc_common_skin_rgbx_Lut`，它在 `chara_tex_bare_common_prelude` 包里，当初没导进来。着色器一直用默认的白图，人物的皮肤和脸偏白偏平。
    - 这次导底模时这张图进来了。我在 `Assets\ROE\common\chara_tex_bare_common_prelude\` 给它加了一个用原引用编号（guid `0a100c…`）的副本，套装和脸也用上了，三者肤色一致，更接近游戏。
    - `stage_bundles.py` 也补上了这个包，以后重新还原时就不缺了。
- **规则表**（每个角色一份 JSON）：
  - `outfit`：放服装的渲染器（和子网格，默认全部）。
  - `groups`：按顺序排，第一条匹配的规则说了算。每条规则有这些字段：
    - `name`，以及中文名 `title`（字幕和检查图用）；
    - `stage`：1 先掉，0 = 不掉；
    - `cloth`：布料（落下、摊平）；不写就是护甲（飞出去、翻滚）；
    - `together`：左右不分开；
    - 条件：
      - `bone`：权重最大的骨头，正则；
      - `anyBone` + `anyShare`：这些骨头加起来至少占多少权重，默认 0.1；
      - `renderer`；
      - `minTriangles` / `maxTriangles`；
      - `lowestBelow` / `lowestAbove`：这块的最低点在绑定姿势里离地多高，单位米。
  - 没有规则匹配的块一直留着。日志里 `stays on (no rule)` 那行按骨头列出这些块，写新规则时照着看。
- **比赛里掉**（`Runtime\RoeClothesBurst.cs`）：
  - **触发**：`FightGame.SpecialHit` 里超必杀的最后一下，和 `Rules` 里的 KO，都调用 `BurstClothes`。
  - **拍快照**：下一次步进时，这一段的每块用 `BakeMesh` 拍下当时的姿势（在 CPU 上蒙皮），然后关掉它的渲染器。
  - **飞出去**：快照变成一个单独的刚体：
    - 重力、空气阻力；
    - 和地面接触：按每块最凸出的约 40 个顶点，用冲量法算反弹和摩擦；
    - 出不了场地的圆圈。
  - **护甲**：沿打击方向 2–4 m/s 飞出，往外 0.8–1.6，往上 1.6–2.8，每秒转 4–10 弧度；掉的时候冒游戏的命中火花。
  - **布料**：初速小、阻力大，落得慢。碰到地面后 0.45 秒内摊平：每个顶点落到离地 0.4–2.9 厘米的"褶皱"高度，双层的布朝下那层再低 2 毫米。
  - **淡出**：2.6 秒后（布料 3.4 秒）用角色着色器自己的抖动淡出（`_IGNOpacity`，阴影一起）消失。
  - **音效**：同一步只响一次命中音效。
  - **两段同时触发**（超必杀直接打到 KO）时，第二段晚 0.3 秒。
  - **可重复**：随机数来自比赛的种子，同一场录像每次一样。
  - **可调**：所有数值都是 `RoeClothesBurst` 上的公开字段，有默认值，可以在检查器里改。
  - **耗时**（批处理里量的）：一段拍快照 0.5–5.6 毫秒；加上第一次生成火花特效，整段最多 26 毫秒。这一下本来就在超必杀最后一下的顿帧（0.12 秒）或 KO 的慢动作里，看不出卡。
- **电脑对手顺手改了一处**：
  - 两人的超必杀和技能 2 在游戏技能表里没有"冲上前"那一段，只有 2.9 米内才打得中（对手半径 1 米 + 1.9 米）。
  - 电脑以前在 4.5 米内就放，2.9–4.5 米之间放的都打空。演示第一次录的时候，在开场的 3.2 米处放超必杀，两边都打空了，爆衣只在 KO 时出现。
  - 现在只在打得到的距离放（`Fighter.SpecialReach`）；会冲上前的技能 1 不变。

给别的角色加爆衣（做法是通用的，代码里没有写死哪个角色）：

1. 照 `a08.json` / `g04.json` 写 `Assets\RoeFighter\Editor\Burst\<id>.json`；
2. 出检查图，看图和日志，改规则直到分组对：
   ```powershell
   .\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','<id>'
   python tools\burst_sheet.py --chars <id>
   ```
3. 重建比赛场景（`RoeFightScene.Build`）。

只能掉"下面有完整皮肤"的块。换别的套装要先量一遍哪里缺皮肤（调研用的脚本在 `tools\research\clothes_burst\`，见 `docs/clothes-burst.md` 第 6 节）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics   # 检查图 → out\clothes_burst\unity
python tools\burst_sheet.py                                                                # 拼成 <id>_sheet.png（含拆分前后的逐像素对比）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics      # 重建场景（拆分在这一步做）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics         # 演示的帧 → _work\burst_demo
python tools\burst_video.py                                                                # → out\clothes_burst_demo.mp4
```

还没做的、已知的：

- 碎片是刚体快照：布料在空中不会飘也不会变形，落地才摊平；同组同侧的链子是一整串一起飞。
- 掉了的裙片，它的骨骼布料链还在模拟（看不见，只多一点计算）。
- 超必杀那一下特效很满，近景里碎片会被特效挡住一部分；KO 那一下看得最清楚。
- g04 腰侧的两个中国结：一个属于内裤，留着；另一个绑在裙饰的骨头上，第一段就掉了。左右不对称，但不太看得出来。
- g04 的底模和她的套装体形差得比 a08 多（贴着皮肤的地方中位 3.9 毫米，a08 1.5 毫米）。穿着衣服时，个别地方可能有身体从贴身衣服里透出来，要在动作里再看。
- 皮肤 LUT 补上以后，所有角色的皮肤和脸都比以前的视频暗一些、红润一些。这是游戏本来的样子；以前的视频是缺图时录的。

### Eve 也能爆衣（10-05）

用户 10-05 问"爆衣效果目前这几个角色都有么"（当时只有 4 个 ROE 角色有），之后说"继续"。先给剑星的两套 Eve 补上：eve09（第 7 代潜水服）和 eve37（办公室装）。

**衣服下面的身体**：
- 剑星本身没有裸体。归档里有一份 mod 的身体（EveOriginalProportions，赤足）：`E:\game_export\StellarBlade\Eve\blend\Nude_Barefoot\`。
- `tools\sb_fbx.py` 只导身体（`SB_FBX_SKIP="Hair|Head"`），放到 `Assets\SB\eve_nude`。
- `tools\sb_textures.py` 用 mod 自带的颜色、法线、ORSS 图。这是新加的：游戏里找不到材质的，按颜色图的名字去 `textures\extra` 找 `_N`、`_ORSS`；粗糙度范围照她自己的皮肤材质。
- `SbFighter.BuildBase` 建材质。
- 它和两套衣服是同一副骨架（189 根骨头都在），但 eve37 髋部那几根差 1.1 cm。

**规则表 `nude` 里新加的三个开关**（ROE 的底模用不到）：
- `pose: "bones"`：底模按自己的蒙皮权重、按骨头名字，搬到衣服的绑定姿势上。eve09 的骨头平均只差 0.01 cm，eve37 最多差 1.11 cm。
- `reveal: true`：这个 mod 的身体比衣服丰满。在 Blender 里量过，臀部和胸部会从紧身衣里穿出来，最多 2.8 cm。没有改身体的形状，而是"被盖住的先不画"：
  - **怎么算被盖住**：顶点正上方（偏 4 mm 以内）、沿法线 4 cm 以内有不透明衣服的三角形，朝向和身体一致，而且这件衣服贴身，或者是一直不掉的部件（鞋、靴底）。被哪几段盖着，就等最后那段掉了再画。
    蕾丝（镂空）、翼片（半透明）不算盖住；宽松的裙子也不算，裙摆底下本来就看得见。
  - **没被盖住、但原来也不露皮肤的地方**：离衣服上看得见的皮肤超过 2.5 cm，就等离它最近的衣服掉了再画；最近的只有不掉的部件，就一直不画。
    eve09 的赤脚比紧身衣的脚往前长出 8.6 cm，脚趾就是这种情况。
  - **皮肤看不看得见**：衣服三角形正好压在上面（0.5 mm 以内）才算看不见。宽一点的话，eve37 鞋带之间的皮肤也算看不见了，脚踝就成了洞。
  - **三角形**：从它三个角里最早要画的那段开始画，所以衣服边上不会有缝。
  - **怎么开**：身体按段拆成几个渲染器（`RoeClothesBurst.reveals`），哪段衣服掉了，就打开对应的那个。
- `fit: true`（eve37）：在一直不掉的部件（鞋）附近，底模贴到衣服自己那层皮肤的形状上，2 cm 内完全贴合，到 4 cm 渐变回去。
  底模的脚是平的赤足，塞不进高跟鞋：不贴的话脚趾从鞋头穿出来，鞋带之间的脚踝要么穿出来，要么去掉后是个洞。

**另外两处**：
- 规则表新加一个条件 `material`（材质名，正则）。Eve 整套衣服是一个网格，紧身衣、两种金属、翼片、蕾丝只能按材质分开。
- `RoeFightScene.MakeDoaFighter`（DOA6、Vindictus、剑星的角色）现在也会拆衣服，有规则表的就能爆衣。

两套掉什么（`Assets\RoeFighter\Editor\Burst\eve09.json`、`eve37.json`）：

| | 第一段 | 第二段 | 第三段 | 第四段 | 一直留着 |
|---|---|---|---|---|---|
| eve09 | 翼片、背后齿轮、前臂护甲：38,689 个三角形 | 领饰和领带、蕾丝：5,957 个 | 手甲、金属护甲：55,764 个 | 紧身衣：17,724 个 | 靴底 |
| eve37 | 领带、吊带：5,724 个 | 百褶裙（连裙腰和扣子）：12,331 个 | 衬衫：16,546 个 | 内裤：2,636 个 | 高跟鞋 |

检查图 `out\clothes_burst\unity\eve09_sheet.png`、`eve37_sheet.png`：
- **穿着时和拆之前比**：eve09 正面 1.8% 的像素不一样，是手臂和手指的皮肤换成了底模的贴图；eve37 是 5.5%，原因见下面一条。
- **看了哪些地方**：正反两面，以及放大的脚和脚踝。没有身体从衣服里穿出来，也没有洞。只看了站姿，打起来的样子看演示。

**领带掉了以后衬衫上的"黑领带"（10-06 查清并修好）**：
- **现象**：10-05 的演示里，eve37 的领带掉了以后，衬衫胸口正中留下一块领带形状、边缘发虚的黑影；裙子掉了以后，衬衫下摆还露出一圈黑边。棚拍的检查图里都没有，只在打斗里有。
- **一个个排除**：
  - 不是几何：演示加 `-roeFaces 1`，把她的材质换成不打光的"正反面"诊断着色器（`Hidden/ROE/FaceSide`：灰 = 正面，品红 = 背面，黄 = 正面但法线朝外）。胸口全是灰的。
  - 不是 SSAO（`-roeSsao 0`），也不是实时阴影（`-roeShadows 0`）：关掉以后黑影一模一样。
  - 衬衫上没有一个顶点挂在领带的骨头上。
  - 检查图也改成摆打斗里的姿势再拍（`-roePose stance`），还是没有。
- **原因**：衬衫的 AO 贴图（`MI_CH_P_EVE_37_Upper_mask.png` 的 G 通道）在门襟上烘了一块领带形状的影子，0.1–0.4，旁边是 0.6 以上。剑星里领带从来不掉，所以看不到。
  打斗场地的灯（`SptLight_Runway_Side`）全是从头顶直直往下照的聚光灯，胸口正面主要靠环境光，而 AO 压的正是环境光，所以几乎是黑的。检查用的主光从正面打，AO 就不明显。
- **修法**（`RoeBurstBuilder.CleanOcclusion`，所有带 `_OcclusionMap` 的材质都适用）：
  - 每块会掉的部件，找出比它先掉、压在它上面的不透明部件。判定和身体的"被盖住"一样：偏 4 mm、沿法线 3 cm、朝向一致。
  - 这些三角形在 UV 上的范围向外扩 1 cm（影子的软边），按这几个三角形自己的像素密度换算成像素。
  - 范围里的 AO 提到周围 2 cm 一圈的中位数，存成新贴图，只给这块部件换一份材质副本（`Generated\<id>\burst\<渲染器>__<材质>.mat`）。
  - 上面那块还在的时候，这里正好被它盖着，所以戴着领带时看不出任何变化。
- **提了多少**：
  - eve37：衬衫 44,520 个像素，平均 0.31 → 0.82（领带底下，还有掖在裙腰里的下摆）；裙子在吊带底下 38,538 个，0.27 → 0.66。
  - eve09：紧身衣在金属甲片底下 670,800 个，0.40 → 0.85；手甲在前臂护甲底下的也提了。
- **修好以后**：同一套演示里，领带掉了以后衬衫前襟是干净的门襟，下摆的黑边也没了（下摆那圈是裙腰压出来的影子）。
- ROE 的四个角色用的是自己的着色器，AO 在另一张图的 B 通道里，这一步还没管它们。
- 诊断图里下摆还有一条很细的品红线，是衬衫里面（三角形背面）。URP 的 Lit 画双面材质的背面时不翻法线（UE 会翻），里面会偏暗。现在看不出来，没改。
- **检查工具新加的开关**：
  - 检查图 `RoeBurstBuilder.Check`：`-roePose 动作@秒`（`stance` 是打斗里的站姿）；`-roeFaces 1`（或一串姿势）每段多拍一张胸口特写和一张正反面图；`-roeKeyPitch 88` 主光从头顶打。
  - 演示 `RoeBurstDemo`：`-roeFaces 1`、`-roeSsao 0`、`-roeShadows 0`、`-roeShots 520`（只录前 520 帧，快速看一眼）。

**顺带发现**：eve37 的腿原来不是"丝袜"。
- 她的身体贴图 `MI_Basebody_V02_F2`（游戏里在 `CH_P_EVE_ReferenceBody` 下，是个普通的皮肤材质）只画了上身和手脚，腿的 UV 落在贴图的填充区，所以腿是红褐色、带旋涡纹。
- 现在腿用的是底模的皮肤，穿着衣服时也是正常的腿。

演示 `out\clothes_burst_demo_eve.mp4`：做法和上面"爆衣"一节的演示一样，对手先走近放超必杀，然后打到 KO，打两局。

### Fiona 也能爆衣（10-06）

用户 10-06："fiona的爆衣先做这个吧，对战的是inase"。Fiona 是 Vindictus 的白羽礼服（PCF_005，`fio005`），演示的对手是 Inase（a08）。
全部细节、数字和一路上看到的问题见 [`docs/vindictus-fiona.md`](docs/vindictus-fiona.md) 第 5 节。

**衣服下面的身体：游戏自己的素体**
- PCF_005 的皮肤在衣服下面都删了；别的服装也补不全（没有一套露出乳头）。
- 游戏里有一个完整的素体 `SM_Fiona_Body01`，是静态网格，以前的导出清单只认骨骼网格，所以没导出过。
  `tools\vdf_nude_export.ps1` 用 UE Viewer 导出（密钥只经临时文件传，用完删除）。
- `tools\vdf_nude.py`（Blender）把它摆成 PCF_005 的姿势、贴到 PCF_005 自己的皮肤上：和它的皮肤中位差 0.3 mm。
  脖子缝到脸部护领下沿，和护领齐平；肤色加上脸的色调，接缝两侧颜色一样；护领下沿的法线渐变到 PCF_005 皮肤的，接缝不显。
- `tools\vdf_fbx.py`、`vdf_textures.py` 导出到 `Assets\VDF\fio_nude`（不入库），`VdfFighter.BuildBase` 建材质。

**爆衣系统新加的**（通用，别的角色也能用）：
- 素体可以有几个子网格、各自的材质：规则表 `nude` 里写 `submeshes` + `materials`。Fiona 的身体和手脚是两张贴图。
- 衣服条目可以按材质名选子网格（`material`，正则），不写编号：FBX 导进来以后子网格的顺序不一定和 Blender 里一样。
- 镂空衣服挡不挡身体按三角形看（`RoeNudeBody.SolidTriangles`）：三角形覆盖的贴图像素 90% 以上不透明才算挡。
  Vindictus 的衣服全是镂空材质，以前一律算不挡；现在礼服主体算挡，蕾丝护腕的网眼不算（身体从网眼里看得见）。
  - 对 Eve 的影响：两套的身体什么时候画，和以前完全一样。eve09 蕾丝上实心的花纹现在也算"压在下面那块上"，
    蕾丝掉了以后，花纹烘在紧身衣和金属甲片上的影子也提亮（紧身衣多 285 个三角形，甲片多 262 个）。

分段（`Assets\RoeFighter\Editor\Burst\fio005.json`）：

| 第一段 | 第二段 | 第三段 | 第四段 | 一直留着 |
|---|---|---|---|---|
| 羽毛头饰、上臂的羽饰：14,282 个三角形 | 蕾丝护腕（连臂环、手链、戒指）、背后的羽饰：17,706 个 | 裙子：5,860 个 | 上衣（连下面的连体裤）：11,621 个 | 高跟鞋 |

- 礼服的后背就是那一片羽饰，所以第二段掉了以后后背全露。
- 检查图 `out\clothes_burst\unity\fio005_sheet.png`：穿着时和拆之前比 7.7% 的像素不一样，是全身皮肤换成了素体的贴图（腿比原来稍亮：PCF_005 的腿调得更深）。
- 演示 `out\clothes_burst_demo_fio005.mp4`：和 Eve 的一样，对手先走近放超必杀，然后打到 KO，打两局。

```powershell
& "D:\Program Files\blender-3.6.15-windows-x64\blender.exe" -b --factory-startup E:\game_export\Vindictus\Fiona\blend\Fiona_BaseBody\Fiona_BaseBody.blend --python tools\vdf_nude.py -- <输出目录>
& "D:\Program Files\blender-3.6.15-windows-x64\blender.exe" -b --factory-startup <输出目录>\fio_nude.blend --python tools\vdf_fbx.py -- Assets\VDF\fio_nude fio_nude
python tools\vdf_textures.py Assets\VDF\fio_nude
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfFighter.BuildBase -Extra '-roeVdf','fio_nude'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','fio005','-roeFaces','1'
python tools\burst_sheet.py --chars fio005
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','fio005','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_fio005'
python tools\burst_video.py _work\burst_demo_fio005 out\clothes_burst_demo_fio005.mp4
```

### 不知火舞的普通攻击（g04 自己的四招，10-03）

用户 10-03 说："luffee的普通攻击改成刚才调研的不知火舞的普通攻击，扇子缩小一点"。

- **动作从哪来**：
  - DOA6 里不知火舞的动作，在 `RRPreview.rdb` 的 `.g1a` 里，用 ripper_tpose 的 `scripts\doa6\g2a.py` 解出来（见那边的 `docs\doa6-fighting-motions.md`）。
  - 新写的 `scripts\doa6\g2a_bvh.py` 把动作写成 BVH：
    - 关节用 Mixamo 那一套名字（Hips、Spine、LeftArm……），这里的通用导入器能认；
    - 零姿势是 G1M 骨架的静止姿势（A 字形）；
    - 根运动并进 Hips 的位移；
    - 写完按 BVH 的规矩算回去和原动作比，关节位置最大差 0.0003 厘米。

  ```powershell
  python E:\code\othercode\ripper_tpose\scripts\doa6\g2a_bvh.py --check --g1m E:\game_export\DOA6\MaiShiranui\g1m_src\MAI_COS_004.g1m `
      --out _work\mocap\doa6_mai E:\game_export\DOA6\MaiShiranui\g1a\MAI00000_MAI.g1a E:\game_export\DOA6\MaiShiranui\g1a\MAI01000_MAI.g1a ...
  ```

- **挑招**：
  - 16 段候选（编号 01000–01600 的打击动作）先导成一个候选包；
  - `RoeMotionPacks.Sheet` 让 g04 把每段做一遍，每段拍 8 帧；
  - `tools\strike_sheet.py` 拼成一张表 `out\motion_sheets\doa6_mai_candidates\sheet.png`，每行写着哪只手 / 脚打、够多远、什么时候能打中。
  - 选了：A 冲步刺拳（01000）、B 中段肘击（01110）、C 扑身冲拳（01002）、D 高踢（01040）。
  - 描述在 `tools\motionpacks\doa6_mai.json`，换招改这里。
- **只给 g04 用**：
  - 动作包加了"只含攻击"（`strikesOnly`），这种包不进 F3 的轮换。
  - `FighterRig.strikePack` 是角色自己的攻击包，`Fighter.moves` 是她的招式表：有自己的就用自己的，没有就用当前动作包的。
  - 建场景时按 `-roeOwnStrikes g04=doa6_mai`（默认）挂上；包还没建的话，从 `tools\motionpacks\<名字>.json` 导入。
  - 走路、站架、侧步还是动作包的。
- **冲步**：
  - DOA 的拳大多带一步冲步，身体往前 0.6–0.7 米。格斗逻辑出招时原本不动角色，招式一收，身体就弹回原位。
  - 现在导入时把这一步从动作里拿出来（`inPlace`），量出走了多远，写进招式（`Move.travel`）。
  - 出招时格斗逻辑带着她按动作的进度往前走，看起来和原来一样，收招不弹回。
- **演示** `out\g04_mai_strikes.mp4`（`RoeClothDemo.OwnStrikes`）：同一套按键脚本（站架、前进后退、侧步、A、C、D、B），左边是原来的普通攻击，右边是不知火舞的。
- **扇子**：
  - `FighterRig.weaponScale` 缩放"带着武器自己那些骨头的那根骨"。g04 是扇子的总控 `All_Fan_ctrl`，在扇轴上，缩了还握在手里。
  - 默认 g04 0.7 倍（`-roeWeaponScale g04=0.7`）。
  - 对比图 `out\weapon_size\g04_fan_sizes.png`：原来 / 0.7 / 0.5，用 `RoeWeaponSize.Stills` 出。
  - 扇子还是只在技能、开场、胜利时出现（用户 10-02 定的）。
- **站架也换了**（用户 10-03 晚："把不知火舞的架势也给 Luffee 当站架"）：
  - DOA6 的 00000 是她的格斗架势，切出 1 秒一个循环（首尾差 0.0004），写进 `doa6_mai.json` 的 `roles.guard`；
  - 角色自己的包里有站架时，`FighterRig.UseMotions` 用它换掉动作包的站架（防御、侧步的上半身也是它），走路、后退还是动作包的；
  - 四招和站架都按刺拳起手时的朝向对齐（`face: stance:mai_jab`），出招时再平滑转向出手方向：从站架出招、收回站架，身体不会再拧一下；
  - 这是个前倾的低架势：躯干前倾 37–41°，髋高 0.78 米（拳击架势是 4°、1.11 米）。DOA6 原始数据里就是 36–43°，转换没有放大（`RoeFightProbe.Lean` 量的）；
  - g05 也是 Luffee（女神套装），默认也用这一套（`-roeOwnStrikes g04=doa6_mai,g05=doa6_mai`）。
  - 对比视频 `out\luffee_mai_stance.mp4`：左边动作包的拳击架势和招式，右边不知火舞的架势和四招，g04、g05 各一段。
  - 10-11 起 g04 的站架改回她游戏里的战斗站姿（见"Luf 站游戏里的姿势；技能键 1、2、3"一节），四招还是不知火舞的；g05 仍站不知火舞的架势。

### 新角色：b10 和 g05（10-03 晚）

用户 10-03 晚："目前b10，G05也把nude补全，加入战斗"。

- **b10 = Kart 的第 10 套**（游戏里的单位 `suit_kart_10`）：红色短夹克、牛仔短裤、大腿枪套、长靴，手拿斧头，技能里用双枪。家族裸体底模 b01。
- **g05 = Luf 的第 5 套**（`suit_luf_5`）：悬空坐着的女神，头后心形光环和两枝金色月桂，白色披帛、长裙片和金饰，光脚。底模是 g01 的 `fm` 版身体（`pc_g01_fm_nk`，用 g01 的皮肤材质）。
- 两个底模都和套装皮肤完全重合（贴合误差中位 0.00 / 0.01 毫米）：套装皮肤就是从这两个身体上裁下来的。

#### 加一个角色的步骤（b10、g05 就是这样加的）

```powershell
# 1. 只拷新角色的包、还原、导入（工程里已有的公共资源保留原来的 GUID，清单里原有的角色保留）
python tools\stage_bundles.py _work\bundles_more b10 g05
python tools\rip.py --bundles _work\bundles_more --out _work\ripped_more --log _work\assetripper.log
python tools\import_ripped.py --src _work\ripped_more\ExportedProject\Assets
# 2. 家族裸体底模（爆衣用）
python tools\stage_bundles.py _work\bundles_nude_b --nude b01
python tools\rip.py --bundles _work\bundles_nude_b --out _work\ripped_nude_b --log _work\assetripper.log
python tools\import_nude.py b01 --src _work\ripped_nude_b\ExportedProject\Assets\AssetBundles --sel _work\ripped_nude_b_sel
# 3. 技能：玩法预制体（拉进约 1000 个依赖包，导入时只拷用得到的）和技能表
copy "<游戏包目录>\gameplay_prefab_suit_kart_10.ab" _work\bundles_skills_more\
copy "<游戏包目录>\gameplay_prefab_suit_luf_5.ab" _work\bundles_skills_more\
python tools\rip.py --bundles _work\bundles_skills_more --out _work\ripped_skills_more --log _work\assetripper.log
python tools\import_skills.py suit_kart_10 suit_luf_5 --src _work\ripped_skills_more\ExportedProject\Assets
python tools\skill_sheet.py suit_kart_10 b10 suit_luf_5 g05
# 4. Unity：只处理新角色（-roeChars），不动 a08、g04 已经拟合好的预制体
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFighterBuilder.BuildAll -Extra '-roeChars','b10,g05'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHumanoidClips.ConvertAll -Extra '-roeChars','b10,g05'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.Run -Extra '-roeChars','b10,g05'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.FitSkirt -Extra '-roeChars','b10,g05'    # 没裙子就跳过
# 5. 爆衣规则表 Editor\Burst\<id>.json，看检查图改到分组对
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','b10,g05'
python tools\burst_sheet.py --chars b10,g05
# 6. 比赛场景（名单默认 a08,g04,b10,g05，-roeRoster 改），exe
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
```

为了能加新角色，改了这些（都是通用的，没有写死哪个角色）：

- **导入保留旧 GUID**（`import_ripped.py`）：AssetRipper 每次导出都给资源新编号，只还原新角色时，包里还带着工程里已有的公共包（家族头部、公共贴图、皮肤 LUT……）。
  现在工程里已有的资源保留原编号、不重新拷，新文件里指向它们的引用改写成原编号，已有的预制体、场景不会断（这次 116 个）。
  清单（`roe_manifest*.json`）合并：这次没有的角色原样保留（`--fresh-manifest` 才只写这次的）。
- **只处理指定的角色**：`RoeFighterBuilder.BuildAll`、`RoeHumanoid.BuildAll`、`RoeHumanoidClips.ConvertAll` 认 `-roeChars`（不写还是全部）。重建人形预制体会丢掉 `RoeHelperFit` 挂上去的组件，所以加角色时不要碰已有的。
- **包的筛选**：套装自己的 `chara_bare_pc_<id>_nk*` 是 H 场景的骨架和动作，不再拷；`--nude g01_fm` 这种 `fm` 版身体要明确点名才拷。
- **武器材质**：一套有几件武器时材质按武器起名（b10 的斧头 `wp_ax` → `wp_b_10_axe_hd`），按渲染器名里的词去配。g05 战斗里没有武器（低模和技能都没用 `wp_g05`）。
- **挂在手臂、腿上的东西**（`RoeHumanoidClips.Convert`）：人形骨架把肢体的扭转挪到下一节，挂在上臂、前臂上的东西就少转了这一下。
  g05 的两条飘带从手臂一直垂到膝盖，末端差了 0.9–1.2 米。现在这些节点的曲线按人形骨架实际摆出的肢体重算：g05 最大误差 1.7 厘米（手指），b10 1.4 厘米；
  扭转辅助骨也一起变准了（之前 1–3 厘米）。a08、g04 后来也重转了（见下面"第二轮"）。
- **飘带交给布料**：`RoeBoneCloth` 按名字认的链加了一类"飘带"（`riband|ribbon`），参数用头发那套轻的，碰撞体用腿和躯干、骨段整段碰；
  `RoeHelperFit` 不再把飘带当辅助骨拟合（g05 的飘带拟合误差 26–85°，硬跟着手臂反而不对）。g05 现在 5 条飘带由布料模拟，第二轮起游戏自己的动作里也是。
- **站姿基准**（`RoeHumanoidClips.Standing`）：格斗里的鞋底高度、脚怎么站、裙子飘带怎么垂，都从角色的站姿里量。g05 的战斗待机是浮在空中坐着（最低的脚骨离地 23 厘米），
  这时自动改用第一个站着的展示动作（g05 是 `idle_02`），日志里写着用的是哪个。检查图、头像、比赛都用它。
- **光脚**：第一版（`-roeBarefoot g05=1`）只是让站脚不套站姿的高跟站法，结果 g05 在动捕里立在脚尖上（芭蕾那样），第二轮改了，见下面"高跟和平底"。
- **治疗技能当攻击**（`RoeSkillSheet.Action.Blows`）：g05 的技能 2 和超必杀在游戏里是治疗（没有伤害数字，只有 heal、regen 这些效果）。格斗里在效果生效的时刻（2.17 秒、2.80 秒）当成一击，
  所以她的超必杀打中也会让对手掉衣服。有伤害数字的技能照旧。
- **脖子补洞**（爆衣规则表 `nude.fill`）：b10 的高领在夹克上，g05 的金项圈连着长裙片，套装的头部网格只到领子上沿，衣服掉了脖子就是个洞。
  现在从底模的头部子网格里挑出"套装头部没有的三角形"（按三个角的位置比对，0.3 毫米内算同一个点）补进身体，用套装脸的材质，权重照样从附近抄。
  b10 补了 784 个三角形，g05 补了 2,060 个；超过底模这块 30% 时认为不是同一个头，不补（日志里写）。

两人掉什么（`Editor\Burst\b10.json`、`g05.json`）：

| | 第一段 | 第二段 | 第三段 | 第四段 |
|---|---|---|---|---|
| b10 Kart | 枪套、腿上的绑带、枪套挂绳、左臂小包：4,046 个三角形 | 红色短夹克和胸前皮带：6,417 个 | 手套和靴子：5,066 个 | 牛仔短裤和腰带：3,884 个 |
| g05 Luf 女神 | 心形光环和月桂枝：9,042 个 | 肩上的白色披帛：2,072 个 | 金饰（手镯、腿环、脚链、戒指）：4,312 个 | 白色长裙片：7,780 个 |

两人都没有"一直留着"的东西，掉完就是全身（b10 光脚，脚就是底模的脚）。

演示：

- `out\clothes_burst_demo_b10_g05.mp4`：b10、g05 各当一次挨打的一方，对手是另一个，打两局，四段全掉；
- `out\fight_cpu_match_b10_g05.mp4`：电脑对电脑一整场（120 秒，g05 二比零，b10 最后全身）；
- `RoeBurstDemo.Run -roeChars b10,g05 -roeFoes g05,b10`、`RoeFightScene.Record -roeP1 b10 -roeP2 g05` 可以重录。

显示名：a08 INASE，g04 LUF，b10 KART，g05 GODDESS LUF，m02 AMANO（10-11）；选人卡片上另有一个词：Valkyrie、Porcelain、Agent、Goddess、Abyss（`RoeFightScene.DisplayNames` / `Outfits`）。

#### 第二轮：g05 的浮空、飘带和脚，a08、g04 重转（10-03 晚）

用户 compact 之后说"继续"，把上一轮列出的毛病接着修掉。

- **g05 在游戏动作里浮空**（`FighterRig.gameHover`）：她的战斗待机悬在空中，游戏里的受击、倒地、三个技能都从这个高度开始（脚离地 23 厘米），
  基础动作却站在地上：每挨一下打，0.04 秒内被抬起 23 厘米，打完再掉下来。量法：`RoeFightProbe.Hover` 把每个游戏动作单独放一遍，
  记最低的鞋底离地多高（四个人里只有 g05 浮空：受击、倒地、技能开头都是 23 厘米，技能里最高到 1.3 米；另外三人都在 0 附近）。现在：
  - 建场景时量出战斗待机浮多高（`RoeHumanoidClips.Standing` 顺便给出，g05 0.23 米，站着的角色是 0），存在角色上；
  - 游戏动作往下放这么多，但不超过"最低的鞋底踩到地"（动作本身再往上升的部分照留），鞋底已经低于站立高度时（倒地躺下）不动；
  - 还有一条：除了脚，身体任何一根骨头离地不少于 10 厘米（`BodyClearance`）。只按鞋底算时，向后倒地腿往上翻、背往下落，
    臀部被压到地面下 8 厘米；加了这条以后倒地和游戏一样（臀部最低 11 厘米）；
  - 技能例外（`NamedClip.hovers`）：游戏的特效是照悬空的身体放的，所以技能保留游戏的高度，开始时 0.3 秒慢慢升上去（`HoverRise`）。
    格斗里技能在最后一击之后 0.8 秒就收招，这时她常常还在半空（技能 1 收招那一刻脚离地 63 厘米），落回站架的时间按高度放长：
    0.3 秒加每米 0.4 秒，最多 0.8 秒（技能 1 是 0.55 秒，`Fighter.StepSpecial`；不浮空的角色还是 0.3 秒）；
  - `-roeHover g05=0` 回到游戏原样，`-roeSkillsHover 0` 让技能也落地。
- **飘带在游戏动作里也交给布料**（`RoeBoneCloth.AlwaysKinds`，现在只有"飘带"一类）：游戏的关键帧把飘带键得几乎是硬的，受击时一米长的飘带像板子一样甩出去。
  现在飘带在任何动作里都模拟，游戏的关键帧当作它的动画姿势（角度限制让它不离开太远），技能里的甩动还在，只是会弯、会拖在后面。裙子和头发在游戏动作里还是游戏的关键帧。
- **a08、g04 按新的肢体重算重转**（`RoeHumanoidClips.ConvertAll -roeChars a08,g04 -roeKeepPrefab 1`）：重转本来会先重建人形预制体，
  拟合好的辅助骨和裙子组件就丢了；`-roeKeepPrefab 1` 用现有的预制体（同一个骨架定义）只重转动作，`RoeHelperFit`、`FitSkirt` 不用重跑，基础动作里的样子不变。
  回放误差（转换前后每根蒙皮骨的位置差，最大值）：a08 从 1.1–2.3 厘米（小腿、前臂的扭转辅助骨）降到 0.3–1.0 厘米（剩下的在手指上）；
  g04 从 1.2–2.8 降到 0.4–2.6（`idle_02` 的裙片 2.6、技能 2 的手指 2.4 没变）。旧的动作文件备份在 `_work\backup_clips_1003\`。
- **高跟和平底分开**（用户 10-03："luf 神的脚是不是不太对，有的角色是有高跟鞋的，有的角色是没有的，得区分一下"）：
  g05 在动捕里一直立在脚尖上（`RoeFightProbe.Feet`：站架脚掌朝下 72–81°，脚踝离地 15 厘米，脚尖扎进地面 2 厘米），游戏自己的开场动作里她是平踩着的（38°、8 厘米）。
  原因：ROE 的光脚在绑定姿势里就是朝下 72° 的"高跟脚形"，人形骨架的 T 姿势保留了这个角度，动捕的平脚套上来就是脚尖。
  - 怎么分（`Editor\RoeFeet.cs`，建场景时量）：把角色站着的游戏动作（`idle_01` 站着时、`idle_02`、`react_01`、`react_02`）逐帧摆一遍，
    每只脚取着地的帧（脚趾骨离地 2.5 厘米内），按脚踝高度从低往高四分之一处那一帧（最低那一帧不可靠：g04 在 `react_01` 里有一帧鞋跟陷进地面，脚踝只有 9 厘米）。
    脚踝还有 10.5 厘米以上就是高跟：a08 16.1、g04 15.1、b10 14.6 厘米；g05 7.7 厘米，是平底。`RoeFeet.Report` 打印四个人的结果。
  - 高跟的照旧（站脚套站姿的高跟角度）。平底的（`FighterRig.flatFeet`）：动捕的脚先绕脚踝往上抬"绑定姿势的角度减去她站着的角度"（g05 左 34°、右 40°），
    站脚摆成那一帧的样子，鞋底高度也用那一帧的。`-roeFeet g05=flat,b10=heels` 可以手动指定。
  - 第二轮的结果：g05 站架脚掌 43°、脚踝 8 厘米（游戏里 38°、8 厘米），走路的支撑脚也平踩着（对比图 `out\g05_feet_flat.jpg`）。
    但脚趾比游戏里多陷进地面 1.5 厘米，角度陡 5°；第三轮查下去，还有后脚整只朝后站、右脚往下压的毛病，见下一节。
- 对比视频 `out\g05_hover_fix.mp4`（`RoeHoverDemo.Run`，左边游戏原样，右边现在）：g05 被 b10 打中两下、放技能 1、被超必杀打倒、躺地、起身。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.Hover -Graphics [-Extra '-roeChars','g05','-roeShots','g05']   # 每个游戏动作离地多高 → _work\hover\hover.txt
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHoverDemo.Run -Graphics                                                   # 对比帧 → _work\hover_demo
python tools\cloth_demo_video.py _work\hover_demo out\g05_hover_fix.mp4 --chars g05 --variants game,floor
```

#### 第三轮：g05 的脚和游戏对齐（10-04）

第二轮留下"脚趾多陷 1.5 厘米、陡 5°"，接着查（`RoeFightProbe.Feet -roeChar g05`，每张照片配一行数字），一共找到四处毛病：

- **被贴地 IK 放到地上的脚，没按站姿摆**：站脚按"脚踝比站立高度高多少"决定套多少站姿（3 厘米内全套，12 厘米以上不套），
  算完之后贴地 IK 才把还差几厘米的脚放到地上。站架里左脚在动捕里高 5 厘米（脚跟抬着），只套了 3/4 的站姿（43°），IK 却把它整个放到地上，
  脚趾就扎进去了。现在 IK 放下多少，就补套多少站姿（`FighterRig.Stand`，IK 之后）。
- **挑站姿那一帧，要整只脚掌着地**（`RoeFeet.SoleDown`）：以前只看脚趾骨着地、脚踝高度，现在还要脚跟那半和前掌那半的皮肤都离地 2.5 厘米以内
  （有这种帧就只在这些帧里挑）。g05 左脚换成 `react_02` 2.10 秒（36°），右脚 `idle_02` 4.80 秒（32°）。
- **站脚的朝向按脚踝的横轴算，不按脚尖**：以前站脚朝哪边，取的是脚尖在地面上的投影。ROE 的脚（光脚和高跟）在绑定姿势里都朝下 67–74°，
  动捕里脚跟一抬，脚尖就过了竖直、指向后方，投影就反了，整只脚掉个头站在地上。现在用脚上下翻转所绕的那根轴（踝关节的横轴）算朝向，
  脚掌朝下多少度都不会翻（脚侧翻到横轴接近竖直时才退回用脚尖）。站架里 g05 后脚的脚尖和膝盖方向从差 146–180° 变成 13–16°；
  着地、膝盖弯着的脚里，脚尖和膝盖反向的从 91 个里 40 个降到 86 个里 1 个（那一个是倒退走的一帧，动捕自己的脚就朝那边）。
- **右脚往上抬，抬反了**（`RoeFeet` 判断转的方向）：平脚在动捕里要先绕脚踝往上抬 36–40°。以前判断方向是"转完以后脚趾骨变高了"，
  可绑定姿势里脚尖朝下 72°，往错的方向转 40° 会越过竖直到 112°，脚趾骨也变高了，两个方向都过。g05 左脚碰巧对了，右脚是往下压了 40°，
  动捕里抬脚、出招时那只脚往后折（脚底朝上）。现在两个方向都试，算连续的角度（过了竖直接着算，大于 90°），选离她站着的角度近的那个。
- 结果（Bandai 包，开场、站架、前进、后退、横移、四个攻击，64 张）：

  | | 站架左脚 | 站架右脚 | 着地脚的脚趾（平均 / 最深） | 陷进地面 2 厘米以上 | 脚跟皮肤离地（平均） |
  |---|---|---|---|---|---|
  | 游戏开场 | 38°，脚踝 8.4，脚跟 1.0，脚趾 −1.0 | 34°，8.0，0.5，−0.6 | | | |
  | 第二轮 | 43°，8.1，2.0，−2.5 | 32°，8.1，5.2，−0.3（脚朝后） | −1.5 / −5.7 厘米 | 33 次 | 3.8 厘米 |
  | 现在 | 36°，7.9，1.5–1.9，−1.2 | 32°，7.9，0.9–1.1，−0.4 | −0.9 / −3.2 厘米 | 3 次 | 2.0 厘米 |

  高跟的三个人只是朝向的算法换了：a08 同一段 128 个脚的数字里只有 3 个变了 1° / 1 厘米以上，都是离地 21–26 厘米的悬空脚。
  对比图 `out\g05_feet_1004.jpg`（左原来立脚尖、中第二轮、右现在：开场、站架、轻拳和重拳收招）。exe 已重新打包，冒烟测试 0 个异常。
- 查的过程中一度以为是辅助骨的毛病：后脚掉头时，脚跟那块皮肤被跟着小腿的辅助骨（`muscle_ankle`、小腿扭转骨）往前带了 6 厘米。
  `RoeFightProbe.HelperHeel` 在她自己的游戏动作里逐帧比过，拟合出来的脚跟皮肤和游戏关键帧只差 0.1–0.2 厘米（最多 1.4），拟合没问题；
  脚摆对以后（站架里膝-踝-脚趾 76–103°，她站着是 112–114°）只剩前腿膝盖压过脚尖时，脚跟皮肤比游戏高 0.5–1 厘米（1.5–1.9 对 1.0），不改。
- 探针多了几项（`RoeFightProbe.Feet`）：脚跟只按脚骨蒙皮的高度、膝-踝-脚趾角、脚尖对膝盖的方向、套站姿之前的脚掌角度和权重、两条腿的关节坐标；
  `-roeSides both` 加拍右侧和正面，`-roeTrack nude_body#6537,nude_body#9396` 逐帧跟一个顶点。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFeet.Report                                                              # 四个人高跟 / 平底、挑中的站姿帧
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.Feet -Graphics -Extra '-roeChar','g05','-roePacks','bandai1','-roeSides','both'   # → _work\feet\feet.txt + 照片
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.HelperHeel -Extra '-roeChar','g05'                          # 辅助骨拟合对脚跟皮肤的误差
```

### 霞（DOA6）和可插拔物理（10-04）

用户 10-04："doa6中的 Kasumi也加入一个角色，看看乳摇，头发，衣服物理是怎么做的，是否比目前的物理做得更好"，
接着"doa5lr 也调研一下……最好做成可插拔的设计，后续我买到magic cloth也能替换"。全部细节、数字、哪些是猜的，见 [`docs/doa-physics.md`](docs/doa-physics.md)。

- **霞**：
  - 模型：DOA6 的默认服装、头发、脸合成一副骨架。
  - 动作：她自己的 DOA6 站架和四个普通攻击（刺拳、侧踢、上勾拳、高位回旋踢）。
  - 游戏动作：受击、倒地、躺地、开场、胜利、三个技能（空翻踢、剪刀脚、手翻扑击，技能表按肢体速度合成）。
  - 没有游戏的特效和语音。
  - 素材放在 `Assets/DOA/kas`（不入库），定义在 `tools/doa/kas.json`，动作包 `tools/motionpacks/doa6_kas.json`。
  - 名单默认加上 `kas`，没有她的文件时自动跳过。
  - 第二套衣服海盗裙是另一个角色 `kas011`（`Assets/DOA/kas011`、`tools/doa/kas011.json`），动作、技能同上。
- **DOA6 的物理**：
  - 胸是体积软体：每边 188 个格点，外面一层壳保持体积，被手臂、大腿顶，网格按格子插值；
  - 马尾、飘带、绳子是骨链；
  - 刘海是摆动骨；
  - 裙子、袖子是粗网格布：默认服装没有，海盗裙 `kas011` 有。控制点网格模拟，看得见的布每帧从 4 × 4 个控制点插值、再加厚度
    （`RoeDoaRig.RebuildSurfaces`，`docs/doa-physics.md` 第 10 节）。
  - 读成 `Assets/DOA/kas/physics.json`（`tools/doa6_physics.py`），由 `DoaPhysicsBuilder` 挂到预制体上（`RoeDoaRig`），`RoeDoaPhysics` 每步模拟。
  - 骨架脚本驱动的扭转辅助骨（`RF_*`）也补上了（`RoeDoaRig.DriveHelpers`）。
- **DOA5LR**：
  - 胸不模拟，按每套衣服选的预设表驱动 7 根骨；
  - 衣服头发是"一个质点就是一根骨头"的全套弹簧网（竖、横环、斜拉、隔一根）；
  - 刘海是姿势混合。
  - 10-04 晚把网格布的弹簧网做成了 F4 的一个方案 `doa5lr_style`：同样的骨链，约束换成 DOA5LR 的横向、斜拉、隔一个、远程弹簧，
    每根弹簧短了和长了各一个刚度（`docs/doa-physics.md` 第 11 节，对比视频 `out\doa5lr_style_compare.mp4`）；胸的预设表没做。
- **可插拔**：
  - F4 换"物理方案"，每个方案给胸、身体、头发、裙子、飘带、链子各指定一个解算器（骨骼布料 / 10-02 旧版 / DOA6 / DOA5LR 式弹簧网 / Magica Cloth 2）。
  - 解算器在某个角色身上做不了的类别交给骨骼布料。
  - 默认 `auto`：霞用 DOA6 的，ROE 角色用骨骼布料。
- **比我们好吗**：
  - DOA6 的胸确实更丰富（会被挤、保持体积）；
  - 头发飘带和我们同一类，但多一个往动画姿势拉回的量，更安静。
  - 软体参数的含义都是猜的：照现在的读法，平常晃得自然（平均偏 0.4–2 厘米），猛的动作会甩出 6–9 厘米，罩杯下缘和胸衣之间有一条细缝。
- 顺手修的老毛病：F4 从 44b174e 起只改了屏幕提示，实际一直是骨骼布料（场景把没设的 `clothBackend` 存成了空字符串）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.DoaFighter.Build -Extra '-roeDoa','kas'                         # 预制体 + 物理（DoaPhysicsBuilder）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','kas','-roeCloths','magica_style,doa6,off','-roeView','chest'
python tools\cloth_demo_video.py _work\cloth_demo out\kas_physics_chest.mp4 --chars kas --variants magica_style,doa6,off
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.DoaPhysicsProbe.Soft -Extra '-roeSteps','30,120,620'              # 软体格点偏离多少、被谁推
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.DoaFighter.Stills -Graphics -Extra '-roeDoa','kas011'           # 静帧 + 布面静止重建对不对
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.DoaPhysicsProbe.Moved -Extra '-roeChar','kas011'                # 两个物理方案把她的节点放得哪里不同
```

### Fiona（Vindictus）（10-04 晚）

用户 10-04："再加入一个角色吧，vindictus里的fiona，用 PCF_005 这个版本，加入进去，注意衣服和头发效果"。全部细节见 [`docs/vindictus-fiona.md`](docs/vindictus-fiona.md)。

- **模型**：ripper_tpose 拼好的 `PCF_005.blend`（游戏的 UE5 骨架，1506 根骨骼）→ FBX + 材质说明 → Unity（`Assets/VDF/fio005`，不入库）。
  材质全部 URP Lit：颜色调整烘进底色图，ARM 图换成 URP 的金属 / 遮蔽 / 光滑度图，眼球的程序化虹膜用 Cycles 烘成图。
- **高跟鞋**：她的绑定姿势是平脚，鞋跟比前掌低 5.3 厘米。建人形骨架时脚尖往下压 32°（按鞋底量的），平脚的动作套上来也踩在鞋跟上。
- **物理**：游戏里裙子、羽毛、头发、胸都是 KawaiiPhysics。参数从游戏包里读出来，算法照插件源码逐行搬（`RoeKawaiiPhysics`）。
  - F4 的 `kawaii`，`auto` 下她默认用它；
  - 别的方案（骨骼布料、DOA5LR 式、关）也都能用在她身上。
- **动作（10-04 夜起是她游戏里自己的）**：
  - 站架、走、退、跑和四个攻击是她自己的招式包 `vdf_fiona`：A 盾后突刺，B 举盾反击斩，C 转身大横斩，D 踢一脚再斩（能击倒）。
  - 受击、击倒 → 躺地 → 起身、拔剑入场、举剑欢呼、三个技能（冲刺突刺、冲刺连斩、多段终结技做超必杀）也是她的。
  - 起身：角色有自己的 `getup` 片段时，倒地后播它站起来（以前是从躺姿直接淡回站架）。
  - 上一轮用的 UFE 2 那套在 git 历史里（8670be9 的 `tools/vdf/fio005.json`）。
- 定义在 `tools/vdf/fio005.json`（和霞的 `tools/doa/*.json` 同一个格式，由 `DoaFighter.Load` 读），名单默认加上 `fio005`。

```powershell
"D:\Program Files\blender-3.6.15-windows-x64\blender.exe" -b --factory-startup E:\game_export\Vindictus\Fiona\blend\PCF_005\PCF_005.blend --python tools\vdf_fbx.py -- E:\code\othercode\roe_fighter_unity\Assets\VDF\fio005 fio005
python tools\vdf_textures.py Assets\VDF\fio005
python tools\vdf_kawaii.py _work\vdf\research\kawaii_params.json Assets\VDF\fio005\kawaii.json   # 游戏的物理参数（读法见 docs 第 3 节）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfFighter.Build -Extra '-roeVdf','fio005'      # 材质、预制体、人形骨架、物理数据，日志里检查碰撞体落点
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','fio005','-roeCloths','kawaii,magica_style,off','-roeView','backright'
python tools\cloth_demo_video.py _work\cloth_demo out\fio005_physics_back.mp4 --chars fio005 --variants kawaii,magica_style,off
```

#### 她自己的动作、剑和盾（10-04 夜）

用户："继续"（上一轮最后提的下一步）。细节见 [`docs/vindictus-fiona.md`](docs/vindictus-fiona.md) 第 4 节。

- **导出**：UE Viewer 能直接导出 UE 5.3 的动画，她有 191 段（`tools/vdf_anims.py export`，`_work\vdf\anim_umodel`，不入库）。
- **转换**（`VdfAnims.Import`）：每帧把游戏的骨骼旋转套到她自己的骨架上，再读成人形动作。
  - 数据的轴向约定（Y 镜像加共轭）是拿没动的骨骼和她的绑定姿势比出来的：550 根骨骼差 0.00°。
  - 放回她身上和游戏原样比：站架的手差 0.2 cm，87 段平均胸口 1.3 cm、手 2.4 cm（肩关节是人形重定向本身的损耗）。
- **`RoeUeRig`**（运行时，每帧）：MetaHuman 的脊柱 5 节、脖子 2 节，人形骨架只有 3 节、1 节。
  - 中间节的弯曲按拟合的比例分回去，胸口误差从 3.2 cm 降到 1.3 cm。
  - 16 根扭转骨按它们在肢体上的位置分滚转，挥剑时手腕翻 160°，前臂不再拧成麻花。
  - UE 骨架的扭转分配改成 (1, 0, 1, 0)：前臂旋转误差从 32° 降到 5.8°。
- **剑和盾**（`tools/vdf_weapons.py`、`VdfFighter.AttachWeapons`）：游戏的 `SK_Longsword01`、`SK_Shield01`，直接读 .psk 建网格。
  - 挂在她骨架里本来就有的 `weapon_r`、`shield_l` 上。
  - 剑上的 `RoeBlade`：用剑手出的招，判定点是剑身上离对手最近的点。
  - 量招用剑尖，剑尖速度 ≥ 5 m/s 算有效时间。
  - 技能的出招时机按剑尖相对身体的速度（≥ 20 m/s）找，开头 0.1 秒不算。
- **招式包新功能**（所有包都能用）：攻击可以只截一段（`from` / `to`），也可以指定击打骨骼（`bone`）。
- **问题**：她的招式前冲大（1–2.5 米），打着打着容易到场边，镜头跑到栅栏外面被挡住。10-05 解决，见"镜头：不跑到场地外面"一节。

```powershell
python tools\vdf_anims.py export                                                        # 191 段 .psa
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfAnims.Import                  # → Assets\VDF\fio005\anims（+ 定义里要的原地版）
python tools\vdf_weapons.py --export                                                     # 剑和盾的贴图、清单
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfFighter.Build                 # 预制体（含 RoeUeRig、剑和盾）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools\motionpacks\vdf_fiona.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfAnims.TwistStills -Graphics   # 前臂扭转骨：不驱动 / 驱动
```

#### 剑和盾跟着游戏的动作走（10-05）

用户："继续"（上一轮留下的第三件事）。细节见 [`docs/vindictus-fiona.md`](docs/vindictus-fiona.md) 第 4 节"剑和盾"。

- 人形动作只有肌肉曲线，剑和盾挂的 `weapon_r`、`shield_l` 没有，上一版一直停在绑定姿势。
- 游戏里盾几乎所有动作都偏 22.9°，举盾反击 73°、重装防御架势 81°、胜利欢呼 121°；剑只在欢呼、喝药这类动作里动（欢呼时反手握剑，95°）。
- 现在转动作时把这几根骨骼每帧的局部旋转和位置也写进去（`VdfAnims.PropBones`：`weapon_r`、`weapon_l`、`shield_l`）。
- 和游戏比，盾的朝向误差平均 26.1° → 5.5°（剩下的是前臂本身的误差），最差 127° → 17°；剑最差 95° → 0°。招式的量法不受影响。
- 前后对比 `out\fio005_props.jpg`。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfAnims.PropStills -Graphics -Extra '-roeTag','before'   # 改之前拍
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfAnims.Import                                            # 重转动作（带剑和盾的曲线）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfAnims.PropStills -Graphics -Extra '-roeTag','after'
python tools\vdf_prop_sheet.py _work\vdf\props out\fio005_props.jpg
```

#### 游戏自己的程序化骨骼：扭转骨、修正骨、手指半关节（10-05）

用户："继续"（上一轮留下的第二件事：肘、膝的修正骨）。细节见 [`docs/vindictus-fiona.md`](docs/vindictus-fiona.md) 第 4 节"游戏自己的程序化骨骼"。

- 动作文件里没有修正骨的关键帧；摆它们的是包里的 `Rig_proc_ControlRig`（一个 ControlRig，逻辑编译成 RigVM 字节码）。
- `tools/vdf_research/controlrig_decode.py` 把它解了出来：常量（450 个属性定义、370 个值）和 460 条指令，反汇编后逻辑和参数都清楚。
- 游戏每帧：先把 257 根手指和修正骨复位到绑定姿势；再按 8 次"算扭转"摆上臂、前臂、大腿、小腿的扭转骨和修正根骨；最后摆 28 个手指半关节。
  - 修正根骨不带扭转，只跟一半（大腿 0.6）的摆动，下面的 `lowerarm_in/out/fwd/bck` 这些就停在关节弯到一半的位置；
  - 扭转骨按各自的比例取手（脚）的扭转，或抵消上臂（大腿）自己的扭转。
- `RoeUeRig` 照这个实现（`TwistRig.Game`，默认；参数都在组件上，默认是游戏的值；上一版按位置分的留作 `ByPosition`）。
- 和游戏比，修正根骨的误差从 19–31° 降到 0–11°，手指半关节 34° → 14°。深蹲、跪地时膝窝的折痕变柔和。对比 `out\fio005_rig.jpg`（左：不驱动，中：上一版，右：游戏的规则）。

```powershell
python tools\vdf_research\zen_dump.py --out _work\vdf\research\raw\controlrig "BaseBody_PCF/Model/Rig_proc_ControlRig"
python tools\vdf_research\controlrig_decode.py _work\vdf\research\raw\controlrig\VindictusRoot\Character\Player\BaseBody_PCF\Model --code > _work\vdf\research\controlrig_code.txt
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.VdfAnims.RigStills -Graphics
python tools\vdf_rig_sheet.py _work\vdf\rig out\fio005_rig.jpg
```

### 选人界面（10-03 晚）

- 场景里放着名单上的所有人（`-roeRoster`，默认 `a08,g04,b10,g05,kas,kas011`，没有文件的自动跳过），开局先进选人界面；没选上的收起来（不显示、不计算）。
- 下面一排卡片，头像是建场景时在影棚里拍的（`Generated\<id>\<id>_portrait.png`）；选中的两人站在场上，播她们的展示待机。上方写着轮到谁选。
- 1P 是人、2P 是电脑（默认）：1P 先选自己的，确认后接着替电脑选；两边都是人就各选各的；两边都是电脑（F1 + F2）就随机选好直接开打。
- 两边选同一个角色时，复制一份那个角色（第一次用到时复制，之后留着）。
- 比赛结束按回车回到这里（上一场的两人还选着，确认两次就是再来一场）；F7 随时回来；F3、F4 在这里按也有效（两人换上新动作包 / 新布料）。
- `ROEFighter.exe -roeP1 b10 -roeP2 g05` 预先选好这两人；`-roeSelect 0` 跳过选人直接开打。
- 演示 `out\select_screen_demo.mp4`（`RoeFightScene.SelectDemo`：批处理里没有键盘，用脚本按同样的步骤换卡片）：1P 从 INASE 换到 LUF（两边都是 LUF 时复制了一份）再到 KART、确认，替电脑从 LUF 换到 INASE 再到 GODDESS LUF、确认，开打。
- 坑：拍头像时开了景深，而影棚的后期设置文件（`Settings\RoeStudioVolume.asset`）比赛场景也在用，焦点 1.45 米的景深就留在了比赛里，exe 整个画面是虚的（批处理录像没受影响）。现在头像不开景深。

#### 选人时播 ROE 自己的展示动画（10-06 晚）

用户说："选人环节也做个动画，ROE里面标准的动画"。

- **ROE 菜单里怎么展示角色**（从游戏包里读出来的）：
  - 每套服装的高模包（`chara_armor_pc_<id>_hd & ld_hd.ab`，菜单里显示的就是这个模型）只带三段动作：`idle_02`（展示待机，循环）、`react_01`、`react_02`。战斗用的动作在另一个包里。
  - 同一个包里有两条 Timeline：`idle02_react01`、`idle02_react02`。每条一个动作轨（播 `react_01` / `react_02`）加一个语音轨（一句台词）。玩家点角色时播其中一条，播完回到 `idle_02`。
  - 台词是按语言换的：`meta_armor_pc_<id>` 把 Timeline 里的占位名（卡地亚是 `Kart_affim`、`Kart_happy_1`）对应到本地化资源（`pc_b10_jp`、`pc_b10_02_jp`），`meta_armor_localize_audio_pc_<id>` 再指向语音包里的音频。游戏的包清单（`Manifest.ab`）写着去哪个语音包找：卡地亚 b10 和 Inase a08 用的是同家族旧服装的语音包（`voice_meta3d_japanese_pc_b03` / `pc_a07`）。
  - 菜单里还有一套 Cinemachine 镜头（9 个虚拟相机），这次没用，比赛镜头不变。
- **四套服装的对应**：

  | 角色 | `idle02_react01` | `idle02_react02` |
  |---|---|---|
  | Inase（a08） | `react_01` 2.57 秒 + Ines EMO_JP_01 | `react_02` 2.48 秒 + EMO_JP_04 |
  | Luf（g04） | `react_01` 3.00 秒 + Luf EMO_JP_10（第 0.13 秒起） | `react_02` 3.00 秒 + EMO_JP_01 |
  | 卡地亚（b10） | `react_01` 3.00 秒 + Kart EMO_JP_04 | `react_02` 3.00 秒 + EMO_JP_03 |
  | 女神 Luf（g05） | `react_01` 3.17 秒 + Luf EMO_JP_01 | `react_02` 3.00 秒 + EMO_JP_29 |

- **选人界面现在这样**：
  - 卡片停在谁身上，谁就在场上转向镜头（从面对面转过来 80%，`FightGame.selectFaceCamera`），循环 `idle_02`。
  - 确认选她时，播她的 `idle02_react01`（`react_01` 加那句"好"一类的应答台词，`selectReaction` 可以换成 `idle02_react02`），播完回到 `idle_02`。
  - 两边都确认后，等反应播完再开打（最多等 3.5 秒，`selectWait`）。
  - 开打后两人照旧面对面，开场姿势照旧是 `react_02`（不带台词）。
  - 霞、Eve、Fiona 不是 ROE 的角色，没有这套展示动画，确认时没有反应，只播待机。
- **做法**：
  - `tools/roe_showcase.py <id>` 从游戏包里读出两条 Timeline 和台词，台词导成 `Assets\ROE\<id>\showcase_voice\*.wav`（游戏素材，不进仓库），名字和时间写进 `tools/roe/<id>_showcase.json`。
  - 建场景时读这个文件，存进 `FighterRig.showcase`，台词加进她的声音表。
  - 选人的逻辑放在每一步模拟里（`FightGame.SelectStep`），所以批处理录的选人画面也有，台词也录进了视频的音轨。
- 演示 `out\select_screen_demo.mp4`（旧的留作 `out\select_screen_demo_before_showcase.mp4`）。

```powershell
python tools\roe_showcase.py a08 g04 b10 g05
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.SelectDemo -Graphics -Extra '-roeOut','E:\code\othercode\roe_fighter_unity\_work\select_demo'
python tools\make_video.py _work\select_demo out\select_screen_demo.mp4
```

### 镜头：不跑到场地外面（10-05）

用户："继续"（上一轮留下的第一件事）。细节见 [`docs/fight-camera.md`](docs/fight-camera.md)。

- **问题**：旧镜头只管构图（两人连线的侧面，离中点 3.6–9.5 米），不管场地。
  - Fiona 的招式前冲大，对局漂到擂台边；两人连线顺着围绳时，镜头就在围绳外面往里拍。
  - 量下来，Fiona 对 a08 那场 31.6% 的帧至少有一人的头、胸或胯被围绳挡住，最长一段连续 38 秒。
- **地图**（`CameraRoom`，建场景时量，存在场景里）：48 米见方、0.25 米一格。
  - 每格做一次盒子重叠检测，看地面以上 0.3–2.6 米有没有场景物体；再算每格离最近的物体多远。
  - 擂台的地图 `_work\camera_room\e23_steel_s02.png`。
- **挑位置**（`FightGame.Frame`，每一步）：依次试正面 → 左右绕开（最多 60°）→ 拉近（视角放大，两人在画面里大小不变）→ 换到两人另一侧（直接切镜头）。
  - 条件：离场景物体至少 0.6 米，到两人的视线不穿过任何东西。
  - 平滑移动的路上如果被挡，直接跳到选好的位置。
  - 全都不行时，把近裁面推过挡在中间的东西（测的几场里没用上）。
- **效果**：会打到场边的三场，被挡的帧 31.6% → 0.2%、9.9% → 0.3%、20.4% → 2.8%；一直在中间打的两场，新旧都是 0%。
  - 剩下的都是有人被打倒、躺在擂台边上：身体伸到围绳下面，或头贴着地面、在擂台 10 厘米高的边沿外，镜头站在场内哪里都会隔着。
- **开关**：游戏里 F8；`ROEFighter.exe -roeCamAvoid 0` 以旧镜头启动。全部参数在场景里 `Fight` 物体的 `FightGame` 上，有默认值（文档第 4 节）。
- **检查**：录像和 `RoeFightScene.CameraStats` 每帧从镜头向两人的头、胸、胯打 6 条射线，用场景网格本身检测，不用地图，
  日志写出被挡的时间段和挡住的物体。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics                    # 重建场景，顺带量地图
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.CameraStats -Graphics -Extra '-roeMatches','fio005:a08:2,g05:fio005:3'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','fio005','-roeP2','a08','-roeSeconds','120','-roeCamAvoid','0','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_camold2'
python tools\camera_compare_video.py _work\fight_camold2 _work\fight_camnew2 out\fight_camera_compare.mp4 --to 70
```

### 剑星的 Eve（10-05）

用户 10-05："另外把剑星里的eve的也加个角色进来，物理能用剑星自己的就用自己的，次选项才是用magic cloth2"。全部细节见 [`docs/stellar-blade-eve.md`](docs/stellar-blade-eve.md)。

- **模型**：ripper_tpose 归档的 `Eve_CH_P_EVE_09.blend`（服装身体 + 头 + 发型 + 马尾，一个骨架）→ FBX（`tools/sb_fbx.py`）→ Unity（`Assets/SB/eve09`，不入库）。
  - 贴图按她游戏里的材质实例重新取（`tools/sb_textures.py`，用 CUE4Parse 读）：颜色、法线、ORM、服装上绿色光线的发光图；披风半透明，蕾丝镂空。
  - 和 Fiona 共用建模型的代码（`VdfFighter.BuildModel`，加了发光贴图）。她的绑定姿势本来就站在高跟上，不用压脚。
- **物理**（F4 的 `stellar`，`auto` 下她默认用它）：全部从游戏的动画蓝图和物理资产读出来（`tools/sb_physics.py`）。
  - 胸、臀、大腿扭转骨、护腕：UE4 的 SpringBone，照 UE 4.26 的源码移植，120 Hz 子步。
  - 前发、鬓发、马尾根、领带：KawaiiPhysics，用的是 Fiona 那次移植的代码；节点改成一个接一个模拟（领带的两段首尾相接）。
  - 马尾后 7 节、披风左右各 7 节：PhysX 刚体链。她有自己的物理场景，刚体在她自己的坐标系里，等于游戏的"本地空间模拟"：整个人移动不甩，身体动作才甩。
    21 个关节按游戏数据摆出的静止位置和她的骨骼全部对上（0.0 厘米、0°）。
  - 没搬的：手臂压胸的 Control Rig、领口一小片 NvCloth、马尾的挡板（见文档 2.6）。
- **效果**：物理对比 `out\eve09_physics_front.mp4`、`out\eve09_physics_back.mp4`（左剑星自己的，中我们的骨骼布料，右关）。
  - 剑星自己的：走路时马尾贴着背小幅摆，回旋踢、转身时整条甩出去再落回来；披风跟着腿和胯摆。
  - 骨骼布料：找到了马尾、头发、胸，但马尾基本直直挂着，踢腿、转身也几乎不甩（按头发的预设，刚度大）；没认出披风。
  - 关：马尾直直垂在背后、披风垂在身体两侧，都只跟着身体走。
  - 电脑对电脑 `out\fight_cpu_match_eve09.mp4`（对 a08，120 秒，修脚之后重录），抽查的画面里没有甩飞。
- **动作**：暂时和 Fiona 第一版一样。站架、走、四个攻击用动作包；受击、倒地、起身和三个技能用 UFE 2 的（技能 1 是 Ethan 的三连击，技能 2 是 Mike 的升龙拳，超必杀是 Mecanim Bot 的鳳翼扇）。
  她游戏里自己的动作（约 4100 段，含剑技连招）还没导。
- **脚（10-05 修）**：用户问"eve的脚是不是有问题"。确实有：站着时鞋底往前倾、离地十几厘米，脚翻到后面去（对比图 `out\eve09_feet_fix.jpg`）。
  - 她的靴子是坡跟厚底，脚在鞋里绷到 77°，几乎和小腿一条线（只差 16°）；绑定姿势下整块鞋底平贴地面，脚踝离地 15.3 厘米。
  - 她的动作全是别人的（动捕、UFE 2）。动捕的格斗站架脚跟是抬起的，这个"再往下绷"加到她已经绷直的脚上，脚就越过垂直线翻到后面：
    站架里左脚尖朝后 157°、右脚 129°，脚踝抬到 30 厘米。格斗逻辑又把这个站架当成"站着时脚该什么样"，每一帧照着摆。
  - 现在角色定义里写 `"feet": "bind"`（`tools/sb/eve09.json`）：
    - 脚和脚趾相对小腿一直保持绑定姿势的角度（她的脚在鞋里本来就动不了），踢腿、迈步时也不会翻；
    - 落地的脚照绑定姿势站：鞋底放平，朝向跟着小腿，按绑定姿势的脚踝高度落地（`FighterRig.bindFeet`，建场景时 `RoeFightScene.BindFeet` 量好）；
    - 借来的"游戏动作"（出场、受击、技能）格斗逻辑原本不做落地，套到她身上整个人陷进地里 13.7 厘米：
      现在她身体立着的时候，鞋底低于地面就整体抬上来，只抬不压（跳起来的招不受影响），躺倒时不动。
  - 检查（`RoeFightProbe.Feet` 贴地拍脚并量鞋跟、鞋尖两半离地多高）：站架、走、侧步、四个攻击里落地的脚，两半离地都是 0.0 厘米；
    出场动作的鞋底在地面下 0.6 厘米。`RoeFootProbe.FootFrames` 把"绑定姿势 / 站架直接套上去 / 格斗逻辑处理后"三步的脚分开量，用来找是哪一步出的错。
- **还没做**：手肘、膝盖的扭转骨和修正骨。游戏里这些骨是逐帧打在每段动画上的，要用她自己的动作拟合，等导她的动作时一起做。
- 定义在 `tools/sb/eve09.json`，名单默认加上 `eve09`。物理演示加了 `-roeView back`（从背后拍全身，给长马尾、披风用）。
- **顺带修了一个所有角色都有的问题**：换物理方案、开新的一局时，新方案把骨骼当时的样子记成静止姿势，可旧方案没先把它动过的骨骼放回去。
  - 所以 F4、F3、选人后再开一局，马尾、头发、链子会从上一局最后一帧的样子开始，而且一直被拉向那个样子（这次 exe 里按 F4 后 Eve 的马尾一直水平支着）。
  - 录物理对比视频时几栏是在同一个场景里依次录的，所以第一栏以外的栏也是接着前一栏的最后一帧开始，"关"那栏并不是建模时的样子。
    以前录的多栏物理对比视频（比如 Fiona 的 `fio005_physics_*`）第一栏以外都受影响，要看准确的可以重录。
  - 现在重建前先让旧方案把骨骼放回静止姿势（`FighterRig.Init`）。修复后同一次录像的第一栏逐像素不变，后两栏变了。

```powershell
& 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup 'E:\game_export\StellarBlade\Eve\blend\CH_P_EVE_09\Eve_CH_P_EVE_09.blend' --python tools\sb_fbx.py -- Assets\SB\eve09 eve09
python tools\sb_textures.py Assets\SB\eve09
python tools\sb_physics.py Assets\SB\eve09
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','eve09'      # 日志里有关节静止位置的检查
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','eve09','-roeCloths','stellar,magica_style,off','-roeView','back','-roeOut','E:\code\othercode\roe_fighter_unity\_work\eve_cloth_back'
python tools\cloth_demo_video.py _work\eve_cloth_back out\eve09_physics_back.mp4 --chars eve09 --variants stellar,magica_style,off
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','eve09','-roeP2','a08','-roeSeconds','120','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_eve09'
python tools\make_video.py _work\fight_eve09 out\fight_cpu_match_eve09.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.Feet -Graphics -Extra '-roeChar','eve09','-roePacks','bandai1'   # 脚贴地拍 + 量
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFootProbe.FootFrames -Extra '-roeChar','eve09'                       # 脚的朝向分三步量
```

### 普通攻击的命中特效（10-05）

用户："普通攻击的效果不用luf的特效了，看有没有其他碰撞的特效"。

- 以前每一下普通攻击打中或被挡，都放同一个特效：g04 技能 2 的命中特效（自动挑的"最小的命中特效"），是一团很大的灰黑色拖影。
- 现在用 UFE 2 自带的格斗命中粒子，按轻重分：
  - 轻（伤害 55 以下）：`Light`，白色闪光加黄色弧线；
  - 中（55 以上）：`Medium`，星形爆光；
  - 重（80 以上或能击倒）：`Heavy`，更大的爆光；
  - 被挡住：`Block`，蓝色斜线。
- 大小分别是 0.35 / 0.45 / 0.55 / 0.4 倍（UFE 做的是 1 米左右，原大会把两个人都盖住），分界和大小都在场景里 `Fight` 物体的 `FightGame` 上，可改。
- 技能自己的特效、爆衣掉甲的火花不变。`RoeFightScene.Build -roeHitFx game` 换回原来的。
- UFE 的这批特效当初没导进工程：`tools/ufe_extract.py` 从下载的 `.unitypackage` 里只读地取出 `Particles` 文件夹和它用到的材质、贴图、网格（78 个文件，按 GUID 跟着引用走），放进不入库的 `Assets/UFE`。
  - 它们用旧的内置粒子着色器（Additive、Alpha Blended），URP 照样能画（这类着色器没有光照标记，URP 当无光照画）。
  - `Heavy`、`Crumple` 里有一层抓屏扭曲（URP 不支持抓屏，它引用的法线贴图也不在包里），会把整个画面画黑一帧：建场景时换成工程自己的 URP 扭曲着色器、强度 0（`RoeHitFx.Prepare`）。
- 对照表 `out\hit_effects_sheet.jpg`（`RoeHitFx.Sheet` 在擂台上两人之间按 0.02–0.45 秒拍，`tools/hitfx_sheet.py` 拼），电脑对电脑 `out\fight_cpu_match_hitfx.mp4`（60 秒）。

```powershell
python tools\ufe_extract.py "E:\Downloads\Universal Fighting Engine 2 Source v2.7.0a.unitypackage" . Assets/UFE/Demos/Shared_Assets/Particles/
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHitFx.Sheet -Graphics
python tools\hitfx_sheet.py _work\hitfx out\hit_effects_sheet.jpg --only ufe_light,ufe_medium,ufe_heavy,ufe_block,ufe_crumple
```

### 不知火舞（DOA6）（10-05）

用户 10-05："把不知火舞也加入进角色来"。和霞同一条路（`docs/doa-physics.md` 第 5 节），细节见第 12 节。

- **模型**：ripper_tpose 早就导好的 `MAI_MaiShiranui.blend`（默认红衣 `MAI_COS_004` + 脸 + 马尾头发）→ `tools/doa6_fbx.py` → `Assets/DOA/mai`（不入库）：
  三副骨架合成一副（1196 根骨），48 个网格、14 个材质。
- **物理**：她游戏里的物理数据（ripper_tpose `g1m_physics.py` 读她的服装和头发 G1M）→ `tools/doa6_physics.py` → `physics.json`，
  `DoaPhysicsBuilder` 挂到预制体上，F4 的 `doa6`（`auto` 下她默认用它）：
  - 胸、臀：4 块格子软体（胸每边约 227 个格点，臀每边约 565 个），网格顶点全部对上游戏（误差 0.00 毫米）；
  - 衣服的垂布：5 块网格布（挂在胯上 1 块 5 × 11、腰上 2 块 5 × 8、胸背 2 块 5 × 6）；马尾也是网格布（3 × 13）；
  - 身后的长穗（白布条加红球，1.2 米）和肩上两条绳：骨链；头发另有 6 根摆动骨。
- **改了一处游戏参数**：长穗那条链的第 8 项（我们读作"每帧往动画姿势拉回多少"，游戏值 0.15）改成 0。
  照游戏值，她一弯腰长穗就横着支出去，和关掉物理一样；DOA6 里它是垂着甩的。改动写在 `tools/doa/mai.json` 的 `tune` 里（游戏值仍是默认，每项写明为什么），
  `DoaPhysicsBuilder` 建物理时套用并写进日志。其余参数都是游戏的。
- **动作**：站架和四个攻击就是 g04 用的那套 `doa6_mai`（A 冲步刺拳、B 中段肘击、C 扑身冲拳、D 高踢）。
  游戏动作用 ripper_tpose `g2a_bvh.py` 新转到她骨架上：受击、倒地、躺地用和霞相同的共用片段；开场 `07110`、胜利 `07020`；
  技能 1 空翻突进（`01004`），技能 2 腾空翻身（`01370`），超必杀是长连段 `02002` 最后往前冲的那一下（第 196 帧起，1.6 秒）。
  - 开场原来用 `07010`，整段要移动 4 米多，开打时她离站位 3.5 米，换成只走 1.2 米的 `07110`。
  - 超必杀先用的是整段 `02002`（4.9 秒）：先后退 1.5 米、站一会儿、再往前冲 4.5 米。技能动作"原地化"只减掉从头到尾的直线位移，
    中段她的身体落在站位后面 3–3.75 米，对打录像里有 4 秒拍不到她。现在只用最后冲的那一段，路线基本是直线。
- **头像**：她的站架低身前倾，脸看不见；角色定义加了 `portrait`（`mai_07020_win@1.3`：胜利动作 1.3 秒，站直举手）。
  给了头像姿势时，镜头对着她的脸（头相对绑定姿势转了多少），不再按脚尖的方向：这个姿势里脚尖朝侧面，第一次拍到的是她的后脑。
- 定义在 `tools/doa/mai.json`，名单默认加上 `mai`。

```powershell
# 1. 模型；物理数据（ripper_tpose）；转给 Unity
& 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup E:\game_export\DOA6\MaiShiranui\blend\MAI_MaiShiranui\MAI_MaiShiranui.blend --python tools\doa6_fbx.py -- E:/code/othercode/roe_fighter_unity/Assets/DOA/mai mai
python E:\code\othercode\ripper_tpose\scripts\doa6\g1m_physics.py -o E:\game_export\DOA6\MaiShiranui\physics E:\game_export\DOA6\MaiShiranui\g1m_src\MAI_COS_004.g1m E:\game_export\DOA6\MaiShiranui\g1m_src\MAI_HAIR_001.g1m
python tools\doa6_physics.py Assets\DOA\mai\physics.json E:\game_export\DOA6\MaiShiranui\physics\MAI_COS_004.json E:\game_export\DOA6\MaiShiranui\physics\MAI_HAIR_001.json
# 2. 游戏动作转 BVH（受击、倒地、躺地、开场、胜利、技能），导进她的动作包
python E:\code\othercode\ripper_tpose\scripts\doa6\g2a_bvh.py --check --g1m E:\game_export\DOA6\MaiShiranui\g1m_src\MAI_COS_004.g1m --out _work\mocap\doa6_mai `
    E:\game_export\DOA6\_common\g1a\CMN00130_CMN.g1a E:\game_export\DOA6\_common\g1a\CMN00081_CMN.g1a E:\game_export\DOA6\_common\g1a\CMN00102_CMN.g1a `
    E:\game_export\DOA6\MaiShiranui\g1a\MAI07110_ENT.g1a E:\game_export\DOA6\MaiShiranui\g1a\MAI07020_WIN.g1a `
    E:\game_export\DOA6\MaiShiranui\g1a\MAI01004_MAI.g1a E:\game_export\DOA6\MaiShiranui\g1a\MAI01370_MAI.g1a E:\game_export\DOA6\MaiShiranui\g1a\MAI02002_MAI.g1a
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/doa6_mai.json'
# 3. 预制体 + 物理（日志里有 tuned 一行），场景，录像
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.DoaFighter.Build -Graphics -Extra '-roeDoa','mai'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','mai','-roeCloths','doa6,magica_style,off','-roeView','back','-roeOut','E:\code\othercode\roe_fighter_unity\_work\mai_cloth_back'
python tools\cloth_demo_video.py _work\mai_cloth_back out\mai_physics_back.mp4 --chars mai --variants doa6,magica_style,off
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','mai','-roeP2','kas','-roeSeconds','150','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_mai'
python tools\make_video.py _work\fight_mai out\fight_cpu_match_mai.mp4
```

## 已知问题和待定的事

- 布料只在基础动作里生效（飘带除外，见"第二轮"）；游戏自己的技能里裙子和头发是手调的关键帧。裙子的参数按 g04 和 a08 量过调过（见"布料"一节）；
  头发、链子、胸还是 Blender 插件的预设。g04 的一条细装饰线在出招时偶尔一帧转 20–30°。
- **裙子**：用户 10-02 晚看过 `out\g04_skirt_fix.mp4` 说"不行，还是有问题"。调研 Magica Cloth 2 之后照它的做法重做了（"布料"一节最后一条，
  `docs/magica-cloth-2.md` 第 9 节）：裙子的动画姿势跟腿走、一片布连成网、惯性分层，F4 可以和旧版对比。等用户看新视频 `out\skirt_magica_style.mp4`。
  之后用户说屁股附近的后片翘起来一点，原因和改法见"布料"一节"屁股附近翘起来"，对比视频 `out\skirt_rest_compare.mp4`。
  10-03 用户问"能自然下垂么"，改成贴着身体自然下垂（"布料"一节"自然下垂"，F5 切换），对比视频 `out\skirt_drape_compare.mp4`。
  还没做：横边在踢腿时还会被拉长（最多 40%）；三角弯曲约束（Magica 网格模式有）没加；g04 前进时后片下摆的毛边还会碰到脚后跟（约 1% 的顶点、1.5 厘米）。
- **TODO 裙子还有一小部分穿模**（用户 10-03 看过 `out\skirt_drape_compare.mp4`："裙子还是不完美，一小部分穿模了，这个先留个todo"）。量到的地方
  （`RoeFightProbe.ButtGap` 的"裙子网格陷进皮肤"，Bandai 包）：g04 后片屁股下方那节（`Skirt_Back_02`）的边缘约 1 厘米，前进时下摆毛边碰脚后跟约 1.5 厘米，
  后退时前片下半（`Skirt_front_02~04`）1–2 厘米；a08 站架、前进、后退为 0，踢腿、侧步、出拳还没逐帧量过。根子：基准姿势已经按皮肤挂好，
  但布料模拟只拿每节骨的尾端 / 骨段去碰腿部胶囊，宽的裙片、厚的毛边在模拟里还会被腿带进去，单条链也没法顺着屁股弯过去。可以试：
  ① 模拟里也用皮肤表面（`RoeBodySurface`）和每节布的采样顶点做碰撞，不只用胶囊；② 一片宽布拆成几条虚拟链，或者补三角弯曲约束；
  ③ 换成按网格顶点模拟（Magica 的 MeshCloth / 代理网格）；④ 先把 `SkirtSwing` 加上皮肤穿透的逐帧统计，所有动作都量一遍再定。
  10-03 拆了 DOA6 的数据：它的裙子就是③的做法。10-04 晚霞的海盗裙照这个做法跑起来了（`docs/doa-physics.md` 第 10 节）。
  10-04 晚③也用 Magica Cloth 2 的 MeshCloth 在 a08、g04 身上做了（F4 的 `magica`，"布料"一节最后一条），④的逐帧量法也有了（`RoeClothPlayDemo` 的量法，
  按脚本每段统计）：a08 布顶点陷进身体超过 1 厘米的全段 0.3%、回旋踢那段 0.9%（骨骼布料 0.4% / 0.7%，关 2.5% / 4.6%）。还没当默认。
  - 每块布只模拟一张粗的控制点网格：不知火舞每块 5 列 × 6–11 行，霞的整圈裙子是 20 × 10 的环。
  - 看得见的布料网格每帧从这张网插值出来，再沿布面法线加厚度。
  - 碰撞体按布分组，每块布只碰附近 6–16 个。

  可以先做便宜的一半：在已经连成网的裙骨上，给布料顶点按网格法线加厚度偏移。详见 ripper_tpose `docs/doa-clothing-and-cloth.md`。
  Magica 适配器是照手册写的草稿，插件装上后第一次编译、试跑时可能要改。
- **爆衣**（10-03，`docs/clothes-burst.md`，"爆衣"一节）：
  - 第一、二步做完了：
    - 超必杀命中、KO 各掉一段，决胜的 KO 全掉，整场保留，F6 开关；
    - 家族裸体底模代替套装皮肤，两人能掉到全身（g04 留着鞋）；
    - 规则表是每个角色一份 JSON，代码对所有角色通用。
  - 还没做：
    - 第三步"打哪破哪"：着色器按命中点挖洞；
    - 烧边；
    - 碎布在空中飘（现在是刚体快照，落地才摊平）；
    - 掉了的裙片停掉它的布料链。
  - 用户 10-03："做完这个demo，还希望把 Riseoferos 其他的角色也加到这个游戏中，可以爆衣，可以适配动作和衣服"，之后再讨论。
    加角色的现成做法见"爆衣"一节最后。要补的主要是每个角色的规则表，以及"哪里缺皮肤"的测量。
- **新角色**（10-03 晚）：
  - g05 的技能 2 和超必杀是把游戏的治疗时刻当成攻击，不是游戏的本意，用户可以改成"给自己回血"；
  - b10 用的是动作包的拳击动作，手里的斧头和双枪只在技能里出现；
  - a08、g04 的辅助骨拟合（`RoeHelperFit`）还是用旧动作拟合的（旧动作里扭转辅助骨差 1–3 厘米），没重跑，基础动作里的样子因此没变。
- 动作包现在有 `bandai1`（默认）和 `accad_male2` 两个，买来的 Unity 动作照"动作包"一节的 JSON 接进来。`accad_male2` 是右脚在前的拳击架势，
  g04 举着大扇子打拳击架势时扇子挡在身前；只有四个攻击键，ACCAD 里还有勾拳、上勾拳、侧踢、前踢、防御、闪避、胜利动作没用上，CMU 的空手道也可以补进来。
- 一个角色播另一个角色的技能时，辅助骨用的还是原角色动作里的数值；`RoeHelperRig` 也可以用在这里，还没接。
- 布料物理：游戏用的是 Magica Cloth 2，但只在大厅模型上模拟头发和饰物（设置都能取出来，`tools\magica_survey.py settings`）；
  战斗里的裙子是手调动画，所以买了插件也没有现成的裙子设置可以照搬（见 `docs/magica-cloth-2.md`）。
- 武器只在游戏自己的动作里出现（放技能、开场、胜利）：a08 的剑挂在骨盆上，基础动作时会跟着拳脚乱甩；g04 的大扇子按用户的要求
  （10-02："小招的时候就不显示扇子了，大招再用扇子"）在站架、走路、普通拳脚时也藏起来，空手打。
- 五个角色着色器照反汇编改过之后（10-02），影棚里的头发比以前亮一些（游戏的头发环境光里带高光项，影棚的环境光又比游戏场景亮）；
  在游戏自己的场景光照下差别不大。对比图在 `_work\shader_compare\`。
- 眼睛的视差按游戏的数值相当强：侧着看时瞳孔会往镜头一侧偏出虹膜半径的三分之一左右，这和真实眼球（虹膜在角膜下约 3 毫米）差不多。
- s01 这个场景的空地只有 7 米宽、40 米长的一条走廊，做能绕圈的 3D 格斗偏窄；已经另挑了开阔的场景（见"开阔的战斗场景"一节）。
- 镜像的角色（屏幕左边的 a08）连带特效一起镜像了，左右手相关的特效方向和游戏里相反；技能本身没有左右之分，暂时看不出问题。
- 命中特效的点光按顶点照到地面上，地面网格稀的地方还会有一块块亮斑；游戏里同样是按顶点算的，先保持一致。
- 场景导入时还没有改写官方包脚本的引用，所以场景里游戏自己的后期（调色 LUT、景深）和灯光附加数据被当成缺失脚本去掉了；`import_skills.py` 的做法搬过去就能用上。
- 战斗动作里脚尖会短暂穿到地面以下 10–20 厘米，这是原动作就有的。
- **霞（DOA6，10-04）**：
  - 软体参数的读法要在游戏里实拍对照；
  - 侧踢起手胸被甩起 6–9 厘米；
  - 罩杯下缘和胸衣之间有细缝；
  - 手掌片碰撞体比游戏的厚；
  - 肘、膝、胯的修形骨和前臂扭转没驱动；
  - 开场从 2.5 米高处跳下，落地时胸顶到上限；
  - 没有游戏特效和语音；
  - 海盗裙：网格布的 46 个参数字只读了 7 个；臀部软体（标志 0x80）怎么读未定，站架里顶在每轴上限；骨骼布料方案里裙子的列和列之间没连起来。

  见 `docs/doa-physics.md` 第 9 节。
- ~~KO 镜头不避墙：KO 发生在场地边上时，镜头会在铁栅栏后面（`out\kas_fight_test.mp4` 第 57 秒）。~~ 10-05 解决，见"镜头：不跑到场地外面"一节。
- exe 开局要等约 17 秒才出画面（10-03 只有四个 ROE 角色时不到 12 秒）：名单上的人都在场景里初始化，两个霞各有 1373 根骨、60 块网格。时间具体花在哪还没量。

## 环境

- Unity 6000.4.12f1，装在 `E:\tools\Unity\Hub\Editor\6000.4.12f1`；Unity Hub 3.22（winget 装的）。选 6000.4 是因为 UFE 2.7.3 在 Asset Store 上要求不低于 6000.4.1。
- 这台电脑直连 Unity 的下载服务器会被转到中国站（国际版安装包 404），只能走环境变量里的代理，而代理单连接只有约 30 KB/s。`tools\fetch_parallel.py` 用 64 个连接分段下载（约 1.8 MB/s），`fetch_fill.py` 补最后几个慢块。
- Git Bash 里的 curl 访问 127.0.0.1 也会走代理而失败，本机服务用 Python（`ar.py` 里关掉了代理）或 PowerShell 访问。
- 环境变量里的代理也会让 Unity 批处理卡在编译：日志一直重复 "Connectivity with IL Post Processor runner cannot be established yet. Retrying."，CPU 是 0，`Unity.ILPP.Trigger.exe` 每十几秒崩溃一次（0xc0000409，`%LOCALAPPDATA%\CrashDumps` 里有转储）。它找本机的编译后处理器，请求被转去了代理。`tools\unity_batch.ps1` 现在启动 Unity 前去掉 `HTTP_PROXY`、`HTTPS_PROXY`，设 `NO_PROXY=localhost,127.0.0.1,::1`（10-17 重启后第一次跑就是这样卡住的）。

### Eve 的百褶裙（eve37）和裙子的 Control Rig（10-05）

用户 10-05："eve有穿jk的衣服么，换成这个角色我看下裙子的物理效果"。全部细节见 [`docs/stellar-blade-eve.md`](docs/stellar-blade-eve.md) 第 6 节。

- **衣服**：她没有真正的 JK 制服（Daily Sailor 是水手领上衣配牛仔裤）。带百褶裙的是 **Office Style**（`CH_P_EVE_37`）：白衬衫、黑领带、
  灰色百褶短裙、丝袜、白色厚底高跟鞋。做成新角色 `eve37`，选人界面里有，默认名单也加上了；建法和 eve09 一样，换成 37 的文件。
- **游戏里裙子怎么动**：衣服的动画蓝图先跑一个裙子专用的 **Control Rig**（`CH_P_EVE_37_Skirt_CtlRig`），再跑 10 个裙片的 KawaiiPhysics。
  - Control Rig 量两条大腿相对骨盆转了多少，按比例把每片裙子的根骨转开：
    - 侧片 0.6 倍；
    - 后片 1 倍，再加另一条腿的一半；
    - 后侧片 0.7–0.8 倍；
    - 前侧片反向 0.3 倍；
    - 前片按两腿之差，差 95° 时转 7°。
  - 然后由 Kawaii 甩下面的部分，腿上有碰撞胶囊。完整的表在文档 6.1。
  - 以前导出 Kawaii 节点用的是蓝图里列出的顺序，不是执行顺序；现在照节点之间的连线排（eve09 不受影响）。
- **怎么搬的**：没有照着表手写，而是**直接跑它的字节码**。
  - `tools/sb_controlrig.py` 把 UE 4.26 RigVM 的内存和指令转成一个程序（写进 `sbphysics.json`）；
  - `RoeRigVM`（C#）照游戏的单元逐条执行 305 步；
  - 骨骼换算成 UE 的坐标给它读写；
  - 跑在弹簧骨之后、Kawaii 之前，和游戏同一个位置。
- **核对**：
  - 游戏资源里存着编辑器上一次运行时每个寄存器的值，拿来重放 305 步，最大差 0.000125；
  - rig 自带的参考骨架和她的绑定姿势换算后对比，全部 0.00 厘米、0.00°。
- **效果**（左：剑星自己的，中：同样的 Kawaii 但不跑 Control Rig，右：我们的骨骼布料）：
  - 视频：`out\eve37_skirt_hips.mp4` 是侧前方的胯部特写，`out\eve37_skirt_back.mp4` 是背后全身；
  - 踢腿放大图：`out\eve37_skirt_kick.jpg`。
  - Control Rig 的作用集中在抬腿：踢腿时大腿上方那几片裙子跟着大腿整片掀开（这一段里最多转 37°）；不跑它时，裙片压在抬起的大腿上。
    平时两列几乎一样。
  - 剑星的裙子摆得开：站架时下摆就往外张，出拳、转身时甩成一圈。我们的骨骼布料贴着腿垂，甩得少。
- **没搬的**：
  - rig 还会挪衣服自己的骨盆（游戏里衣服是单独的网格）。这里衣服和身体共用一根骨盆，挪了整个人都会动，所以跳过；它想挪多少记在日志里（这一段最多 2.2 厘米）。
  - 手臂压胸的 `BtoB_CtrlRig` 也没搬，现在能反汇编了，以后可以用同一个虚拟机跑。
- **顺带改的**：格斗逻辑里给 ROE 角色用的"挂在骨盆上的裙片按站架重新摆"那条规则，以前也作用在 Eve 身上（eve37 的后片、后侧片和绳子）。
  剑星的裙子不是手 K 的，`stellar` 方案现在不用这条规则。
- 演示工具：
  - 新的对比列 `stellar_norig`（剑星自己的，但不跑 Control Rig）；
  - 新机位 `-roeView frontright` / `frontleft`（从前面拍胯部特写）。

```powershell
python tools\sb_physics.py Assets\SB\eve37 --outfit CH_P_EVE_37          # 连同裙子的 Control Rig 程序
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','eve37'   # 日志里有重放和轴向检查
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','eve37','-roeCloths','stellar,stellar_norig,magica_style','-roeView','frontright','-roeOut','E:\code\othercode\roe_fighter_unity\_work\eve37_rig_frontright'
python tools\cloth_demo_video.py _work\eve37_rig_frontright out\eve37_skirt_hips.mp4 --chars eve37 --variants stellar,stellar_norig,magica_style
```

### Inase 的裙子：游戏里怎么动的（10-05）

用户 10-05："inase的裙子再看一下，先看游戏里的物理是怎么控制的，是否有比magic cloth处理更好的方式"。细节见 [`docs/inase-skirt.md`](docs/inase-skirt.md)。

- **游戏里没有物理**：a08 的战斗、展示包里只有动画组件；裙子 34 根骨在她 10 个游戏动作里都是逐帧手 K（技能里后片翻起 111°）。
  Magica Cloth 2 只在大厅里甩头发、挂件和裙子上的链子。
- **手 K 的裙子是甩出来的**：
  - 拿腿的姿势去预测裙子（线性、剑星式"抬腿才推"、最近邻），站着的动作能预测（5–11°），技能里都预测不了（44–58°，和不动一样）；
  - 工具 `tools/skirt_key_models.py`。
- **拿她自己的技能当标准答案比**：
  - 新的检查开关 `FighterRig.IgnoreSkirtKeys`：游戏动作里也不用裙子关键帧，并量和关键帧差多少；演示用 `-roeScript skills -roeSkirtKeys off`，新对比列 `legs`；
  - 结果：我们的骨骼布料 62.6°、Magica Cloth 2 61.4°、只跟着腿 61.3°；
  - 视频 `out\a08_skirt_vs_keys.mp4`（左到右：游戏手 K / Magica / 我们的 / 只跟着腿）：游戏里长裙片大幅飘开，三种方案都贴着腿垂。
  - 差在目标（我们是照"自然下垂、踢腿不甩飞"调的），不在用哪个插件。
- **建议（等你定）**：拿游戏关键帧当目标自动调物理参数，可以和现在的"自然下垂"做成两套切换；逐个动作离线烘成关键帧可以放在后面。
  游戏自己的动作（技能、受击、出场、胜利）一直是直接用游戏的裙子关键帧。

```powershell
python tools\skirt_key_models.py a08
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','a08','-roeCloths','magica_style,legs','-roeScript','skills','-roeSkirtKeys','off','-roeOut','E:\code\othercode\roe_fighter_unity\_work\a08_skills_nokeys'
```

### ROE 战斗里有没有物理（10-05）

用户 10-05："战斗的场景也没有物理是吧，都调研一下，ROE内的"。细节见 [`docs/roe-battle-physics.md`](docs/roe-battle-physics.md)。

- **战斗里没有物理模拟。**
  - 战斗角色 266 个包、敌人 115 个、技能数据 231 个：一个物理组件都没有。角色身上会动的头发、胸、裙子、挂件全是关键帧。
  - 战斗舞台 194 个包、舞台的环境模型 1427 个：只有不会动的碰撞体（给点击、手势做射线检测，给粒子当地面），没有刚体、关节、布料，也没有 Magica。
    少量粒子开了碰撞（落叶落地）或被力场吹（风沙）。
  - 特效 3058 个包：4.6 万个粒子系统里 107 个开了碰撞，大多是回忆模式里的液体。
- **真正的物理只在大厅**（Magica Cloth 2）：头发、挂件，还有时装配件上的 21 个（头纱、蝴蝶结、尾巴、流苏……）。这些时装没有战斗模型。
- 工具 `tools/roe_physics_scan.py`：一共 5998 个包，扫 22 秒，`--report` 按类别汇总。

```powershell
python tools\roe_physics_scan.py
python tools\roe_physics_scan.py --report
```

### Inase 完全用 Magica Cloth 2（10-05）

用户 10-05："inase完全用magic cloth 2 我看看效果吧"。细节见 [`docs/inase-skirt.md`](docs/inase-skirt.md) 第 6 节。

- **新方案 `magica_full`**：
  - Inase 的裙片、头发、胸、链子全交给 Magica Cloth 2，参数全用插件自带的预设（Skirt、FrontHair / ShortHair、胸用弹簧模式 MiddleSpring、Accessory）；
  - 技能、受击这些游戏动作里也由它来动；
  - 不用我们的"自然下垂"。
- **默认**：装了 Magica 插件时，Inase 在游戏里（auto）默认就用它，F4 可以换回别的；没装插件时不变。
- **效果**（左 Magica 全接管，右原来的）：`out\a08_magica_full_front.mp4`（正面）、`out\a08_magica_full_backright.mp4`（背后特写）。
  - 走路、侧步时长裙片往身后飘，回旋踢时大幅甩开；后片在屁股上折出褶子，踢腿时折着让开大腿。原来的方案几乎一直垂着，后片像板，踢腿时腿会穿过去。
  - 代价：穿进身体的布料多一点（1.1% 对 0.4%），抖动大一点；技能 1 开头人被动作一下子带走时，裙子有一处很大的跳动。

### 身体检查：爆衣、乳摇、权重、Inase 的头发和衣服（10-05）

用户 10-05："继续，爆衣效果目前这几个角色都有么"，"爆衣，乳摇，都身体权重，inase的头发也衣服，这些都检查一下"。细节见 [`docs/body-check.md`](docs/body-check.md)。

- **爆衣**：4 个 ROE 角色（a08、g04、b10、g05）有；霞两套、不知火舞（DOA6）、Fiona（Vindictus）、Eve 两套（剑星）还没有。
- **乳摇**：10 个都有胸部物理（ROE 是骨骼布料、Inase 在游戏里是 Magica，DOA6 是软体，Fiona 是 KawaiiPhysics，Eve 是剑星的弹簧骨）。
- **权重**：转胸骨时动的都只有胸，没有漏到胳膊、脖子、肚子、背上；衣服和下面的身体基本一起动。
- **修了 Inase 的两处**：
  - Magica 自带的弹簧预设（MiddleSpring）太软：胸平均离开动画 4 cm、最大 12 cm，站着也有 2.6 cm，胸被顶得变形。
    现在默认 HardSpring、力 0.3、最多 2 cm、惯性 0.6：站着 0.7 cm，技能里平均 1.9 cm、最大 6.5 cm（游戏手 K 的是 1.5 / 4.9）。
  - 胸甲和下面裸体的权重对不上，胸一动胸甲就滑开。爆衣规则加了 `followBody`：胸甲下面的身体从周围皮肤补权重，胸甲再抄身体的。
- 工具：
  - `RoeBodyCheck`（菜单 `ROE Fighter/Checks/Body check`）：转胸骨、烘网格，看什么跟着动，出报告、胸部特写和热度图（`tools/body_check_sheet.py`）；
  - `RoeClothPlayDemo` 加了 `-roeView chest|hair`、`auto:nude`（衣服全掉）、胸的读数（`breasts.txt`）和头发穿模读数（`hair.txt`）；
  - `tools/takes_grid_video.py`：几段录像拼成网格视频。

### Luf 的裙子：四种物理对比（10-06 晚）

用户 10-06 晚："luf的裙子的物理也检查一下"；又问各角色用的什么物理，是不是 Vindictus 的最好、其次 Magica Cloth 2。

- **怎么比**：
  - 播放模式录像（`RoeClothPlayDemo`，Magica 只在播放模式里动），g04（Luf）和 g05（女神 Luf）各录四种：
    - 我们的骨骼布料（`magica_style`，两人现在的默认）；
    - Magica Cloth 2 用我们的参数（`magica`：g04 的三块裙片是 MeshCloth，其余骨链是 BoneCloth）；
    - Magica Cloth 2 全接管、用插件自带的预设（`magica_full`，Inase 现在用的）；
    - 关（`off`，只跟动画）。
  - 同一段动作：站架、前进、后退、侧步、四个普通攻击（不知火舞的那四招），正面和右后两个机位；
  - 另录了选人界面播的展示动作（`-roeScript showcase`：idle_02、react_01、idle_02、react_02，游戏自己的动作）。
- **读数**（`_work\luf_skirt_front\metrics.txt`，全段平均；骨骼布料和关每次结果一样，Magica 每次略有出入，右后机位那次差不多）：

  | | 骨骼布料 | Magica（我们的参数） | Magica（插件预设） | 关 |
  |---|---|---|---|---|
  | g04 穿进身体（顶点超过 1 cm 在身体里的比例） | 0.46% | **0.32%** | 0.80% | 0.83% |
  | g04 抖动（每步的二阶差分，毫米；关的那一列是动作本身带的） | 9.1 | 7.6 | 8.0 | 2.5 |
  | g04 网格拉伸：平均（最坏一步里排前 1% 的边） | **2.5%（67%）** | 3.9%（129%） | 5.0%（150%） | 2.1%（59%） |
  | g05 穿进身体 | **0.19%** | 0.37% | 0.62% | 0.46% |
  | g05 抖动 | 11.4 | **7.3** | 9.5 | 5.5 |

- **看画面**（`out\luf_skirt_compare.jpg`，从左到右就是上表四列）：
  - **g04**：骨骼布料的裙片完整、自然下垂，高踢时前片垂在支撑腿前面，冲拳时后片贴着屁股。
    - Magica 两种设置都把左边那条窄裙片（MeshCloth `left ribbon`，在 `Skirt_Left_01` 上）拉成几根细条，表里最坏拉伸 129–150% 就是它；
    - 刺拳、冲拳时后片上端离开屁股掀起来（10-02 用户说过的"屁股附近翘起来"那样）；
    - 插件预设在高踢时还把前片甩起来揉成一团；
    - 关的时候前片僵硬地跟着踢起的腿斜出去。
  - **g05**：她没有裙子，是飘带和长发。
    - 骨骼布料穿模最少，但抖动最大（侧步 7.1 对 Magica 的 1.6）：两侧飘带在动画给的"飘起来"和重力的"垂下去"之间来回摆，身后的长飘带垂到地上，末端在地上翻折；
    - Magica（我们的参数）飘带多半垂着，更平静；
    - 插件预设把头发和飘带甩得很乱（这一条录像的动作和另三条差几帧，没完全对齐）。
  - **选人展示动作**（游戏自己的动作，`out\luf_showcase.mp4`）：g04 的裙子在游戏动作里本来就照游戏手 K 的关键帧（骨骼布料、Magica 我们的参数、关三列一模一样）；
    只有插件预设在游戏动作里也模拟：react_01 抬膝时前片盖到膝盖上，美术手 K 的是撩开、露出腿。g05 的飘带三种都垂着、跟关键帧差不多。
- **结论**：两个 Luf 都留在骨骼布料，`auto` 不变（Inase 仍是 `magica_full`）。Magica Cloth 2 在 Inase 的长裙片上好，在 Luf 的窄裙片上会拉出细条。
- 工具的改动：录像加了 `-roeScript showcase`（脚本里的 `clip:<名字>` 一步让她播那段动作，`RoeClothDemo.PlayScriptClips`）；
  `tools/cloth_demo_video.py` 不给 `--chars` 时按 `takes.txt` 里录了的人拼。

```powershell
# 四种物理并排：正面、右后、选人展示动作（播放模式，每次约 7 分钟）
.\tools\unity_batch.ps1 -NoQuit -Graphics -TimeoutMinutes 50 -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run -Name luf_skirt_front `
    -Extra '-roeChars','g04,g05','-roeCloths','magica_style,magica,magica_full,off','-roeScript','moves','-roeView','front','-roeOut','_work\luf_skirt_front'
python tools\cloth_demo_video.py _work\luf_skirt_front out\luf_skirt_front.mp4 --variants magica_style,magica,magica_full,off
# 右后：-roeView backright；展示动作：-roeScript showcase
```

### 天野（m02）加入战斗；原地放的技能按游戏的距离打（10-11）

用户 10-11："继续，把m02也加入战斗，战斗动作也加入进来，作为3个技能使用，之前的inase 和luf的也是，战斗技能都作为3个技能加入进来"。

- **m02 = 天野（Amano）的第 2 套**（游戏单位 `suit_amano_2`）：身上是画在皮肤上的黑色藤蔓，背后一对发蓝光的翅膀，头顶到身后一圈黑色触手，
  手里一串漂浮的眼睛（她的武器），光脚。家族裸体底模 m01。显示名 AMANO，选人卡片上的词 Abyss。
- **三个技能**：和其他 ROE 角色一样，游戏的 `skill_01`、`skill_02`、`skill_03` 就是格斗里的技能 1、技能 2、超必杀
  （L、O、P，或 236 / 214 / 236236 + J/U；超必杀要满能量槽），由她自己的时间轴驱动，特效、音效、语音都是游戏的。
  Inase、两个 Luf、Kart 本来就是这样接的（`RoeFightScene` 的 `game.specials`）。
- **原地放的技能以前大多打空**（四个 ROE 角色都有）：技能的伤害只在对手离她不超过"对手半径 1 米 + 1.9 米"时算数。冲到对手面前再打的技能没问题，
  但很多技能在游戏里是原地放的：扔剑、开枪、触手从地里冒出来，打的是站在远处的敌人。对手离开 2.9 米，这些技能的特效明明打到了她，伤害却算打空。
  游戏把这类技能的命中特效摆在她前方固定的距离（游戏里敌人站的那一排），现在就按这个距离算：
  原地放的技能能打到"命中特效离她多远 + 对手半径"，至少还是原来的 2.9 米。
  - 时间轴里被静音的命中轨道也算：游戏把一部分技能改成在目标身上生成命中特效，原来那条轨道静音了，但它的挂点还标着敌人站在哪。
  - 冲到对手面前的技能（游戏里有一段滑到对手攻击位的位移）照旧；没有命中特效的（Luf 女神的两个治疗技能）也照旧。
  - 开始放的条件不变（离对手不超过技能的范围 6 / 7 米）；电脑也按新的距离决定什么时候放。
  - 代码：`RoeSkillSheet.Action.HitsAhead`（命中特效离她多远）、`Action.Dashes`（有没有冲刺）、`FightGame.BlowReach`（打击距离）。

  | 角色 | 技能 | 冲到对手面前 | 游戏把命中特效放在她前方 | 以前打得到 | 现在打得到 |
  |---|---|---|---|---|---|
  | m02 天野 | 技能 1、技能 2 | 冲 | — | 冲到后 2.9 米 | 不变 |
  | m02 天野 | 超必杀（触手从地里冒出来） | 不冲 | 6.3 米 | 2.9 米 | 7.3 米 |
  | a08 Inase | 技能 2（扔剑） | 不冲 | 9.8 米 | 2.9 米 | 10.8 米 |
  | a08 Inase | 超必杀 | 不冲 | 9.5 米 | 2.9 米 | 10.5 米 |
  | g04 Luf | 技能 2 | 不冲 | 6.1 米 | 2.9 米 | 7.1 米 |
  | g04 Luf | 超必杀 | 不冲 | 4.7 米 | 2.9 米 | 5.7 米 |
  | b10 Kart | 技能 1、技能 2（枪） | 不冲 | 6.0 米 | 2.9 米 | 7.0 米 |
  | g05 Luf 女神 | 技能 1 | 不冲 | 6.9 米 | 2.9 米 | 7.9 米 |
  | g05 Luf 女神 | 技能 2、超必杀（游戏里是治疗） | 不冲 | 没有命中特效 | 2.9 米 | 不变 |

- **技能录像**（`RoeFightScene.SkillReel`）：真实的格斗逻辑里，1P 依次放技能 1、技能 2、超必杀（放超必杀前把能量槽加满），2P 站着不动、谁都不是电脑；
  每招等两人都回到架势再放；够不着的技能先走近（和电脑一样）。日志里每招写着两人相距多远、打击距离多少、打中后对手剩多少血。
  `tools/skill_reel_video.py` 给每段配上声音和字幕（谁、第几个技能），再接成一个视频。
  `out\skills_reel.mp4`（68 秒）：m02、Inase、Luf、Luf 女神各放三个技能，12 招都打中：技能 1 掉 150、技能 2 掉 190、超必杀掉 360 并击倒，
  对手的衣服掉一段。
- **加入 m02 时改的**（都是通用的，没有写死哪个角色）：
  - **着色器**：她的翅膀和触手用游戏的 `CharacterVFX`（角色着色器加溶解、菲涅尔、扭曲，这套衣服里都关着），对到我们的 `RoeCharacter`（`import_ripped.py`）。
  - **动作名**：游戏把她的展示动作拼成了 `recat_01/02`，按 `react_01/02` 认（`CLIP_ALIASES`）。
  - **武器的骨骼**：漂浮的眼睛挂在 `Wp_eyes_01..18` 上，这组骨骼只有战斗低模才有。建预制体时把缺的骨骼按低模的父子关系和本地姿势补进高模
    （`RoeFighterBuilder.GraftBone`），游戏动作照常驱动它们（待机里也有关键帧）。和其他角色的武器一样，眼睛只在技能、开场、胜利时出现。
  - **爆衣三段**：触手环、翅膀、身上的藤蔓。藤蔓画在皮肤贴图里（套装的皮肤材质用的就是 body1 那张带藤蔓的贴图），
    所以第三段是把整块套装皮肤原地淡出（游戏自己的网点淡出 `_IGNOpacity`，0.8 秒），下面露出 m01 的身体，不往外飞。
    - 爆衣规则新加 `fade`（`Editor\Burst\m02.json`、`RoeClothesBurst.Piece.fade`），皮肤着色器 `RoeSkin` 也加了网点淡出。
    - 三种材质各写一条 outfit：它们在接缝处共用点，写在一条里会焊成一整块，哪条规则都对不上。
    - 检查图 `out\clothes_burst\unity\m02_*.png`；比赛里的演示 `out\clothes_burst_demo_m02.mp4`（Inase 的超必杀打掉触手环，KO 打掉翅膀；
      第二局的超必杀让藤蔓淡出）。

```powershell
# 1. 拷包、还原、导入（AssetRipper 服务开着，见"从零重做一遍"）
python tools\stage_bundles.py _work\bundles_m02 m02
python tools\stage_bundles.py _work\bundles_nude_m --nude m01
python tools\rip.py --bundles _work\bundles_m02 --out _work\ripped_m02 --log _work\assetripper.log
python tools\rip.py --bundles _work\bundles_nude_m --out _work\ripped_nude_m --log _work\assetripper.log
python tools\import_ripped.py --src _work\ripped_m02\ExportedProject\Assets
python tools\import_nude.py m01 --src _work\ripped_nude_m\ExportedProject\Assets\AssetBundles --sel _work\ripped_nude_m_sel
# 2. 技能：玩法预制体和技能表
copy "<游戏包目录>\gameplay_prefab_suit_amano_2.ab" _work\bundles_skills_m02\
python tools\rip.py --bundles _work\bundles_skills_m02 --out _work\ripped_skills_m02 --log _work\assetripper.log
python tools\import_skills.py suit_amano_2 --src _work\ripped_skills_m02\ExportedProject\Assets
python tools\skill_sheet.py suit_amano_2 m02
# 3. Unity（她没有裙子，FitSkirt 跳过）；场景名单默认已含 m02
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFighterBuilder.BuildAll -Extra '-roeChars','m02'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHumanoidClips.ConvertAll -Extra '-roeChars','m02'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.Run -Extra '-roeChars','m02'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','m02'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
# 技能录像（每人约 1 分钟）和合成
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.SkillReel -Graphics -Extra '-roeP1','m02','-roeP2','a08'
python tools\skill_reel_video.py out\skills_reel.mp4 _work\skills_m02 _work\skills_a08 _work\skills_g04 _work\skills_g05
# 爆衣演示
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','m02','-roeFoes','a08','-roeOut','_work\burst_demo_m02'
python tools\burst_video.py _work\burst_demo_m02 out\clothes_burst_demo_m02.mp4
```

踩过的坑：Unity 批处理偶尔报 "Scripts have compiler errors"，日志里却是 "Internal build system error. Read the full binlog without getting a
BuildFinishedMessage"、没有任何 `error CS`：这是 Unity 编译系统自己的偶发错误，原样重跑就好。

### Luf 的衣服：和游戏里比（10-11）

用户 10-11："luf的衣服目前是什么物理效果"，然后"再看看游戏里的效果是什么，看看差异"。做法和 Inase 那次一样（[`docs/inase-skirt.md`](docs/inase-skirt.md) 第 3 节）。

- **游戏里怎么做的**：
  - 战斗里没有物理（见"ROE 战斗里有没有物理"）：裙子、飘带、头发、胸在每个动作里都是手 K 的关键帧。
  - 大厅里只有头发用 Magica Cloth 2（BoneCloth）：
    - g04 是刘海和四条发束（`Bip001 bangs_L/R`、`hair_BL/BR/FL/FR`，7 个胶囊碰撞体）；
    - g05 是脑后三条（`Hair_BC/BL/BR_01`，5 个胶囊和 1 个球）。
    - 裙子和飘带在大厅里也是手 K。
  - 手 K 甩多大（`tools/cloth_key_stats.py`）：每根骨的方向放在骨盆坐标系里，和她的战斗站姿（`idle_01` 第一帧）比。
    每根骨取它在这段动作里最大的那一下，表里是"所有骨的平均 / 最大"：

    | 动作 | g04 裙子（30 根） | g04 头发（18 根） | g05 飘带（23 根） | g05 头发（25 根） |
    |---|---|---|---|---|
    | 站架 idle_01 | 12° / 42° | 7° / 21° | 29° / 70° | 18° / 31° |
    | 待机 idle_02 | 38° / 67° | 39° / 51° | 61° / 135° | 35° / 59° |
    | 反应 react_01、react_02 | 44–53° / 72–76° | 48–59° / 62–75° | 78–94° / 166–170° | 45–47° / 69–70° |
    | 受击 hurt | 60° / 131° | 75° / 101° | 99° / 179° | 71° / 96° |
    | 技能 1、技能 2、超必杀 | 104–119° / 176–179° | 99–112° / 131–162° | 120–124° / 176–177° | 79–87° / 113–122° |
    | 倒地 die | 66° / 174° | 129° / 168° | 115° / 173° | 89° / 107° |

    技能里裙片和飘带几乎整片翻过去：是跟着转身、踢腿甩出来的，不是挂着往下垂。

- **我们现在怎么做的**（F4 默认，两个 Luf 都是我们的骨骼布料 `magica_style`）：
  - 走、侧步、普通攻击这些动捕动作，游戏里没有，衣服和头发由骨骼布料模拟（目标是"自然下垂、踢腿不甩飞"）。
  - 游戏自己的动作（技能、受击、倒地、开场、胜利、选人展示）：g04 的裙子、两人的头发直接用游戏的关键帧，所以和游戏一模一样；
    只有 g05 的飘带一直模拟（`RoeBoneCloth.AlwaysKinds`：她的飘带在游戏动作里几乎是硬的，受击时像木板一样甩出去一米），
    模拟时以关键帧的姿势为准、角度限制把布拉在它附近，所以技能里的旋转还在，只是会弯、会拖。

- **并排比**：在她们自己的技能 1、技能 2 里（`-roeScript skills`，12.6 秒），把关键帧关掉交给物理（`-roeSkirtKeys off`，这时头发也交给物理），
  和游戏手 K 并排。视频 `out\luf_cloth_vs_game.mp4`：上排 g04、下排 g05；左到右：游戏手 K / 我们的骨骼布料 / Magica Cloth 2（我们的参数）/
  Magica Cloth 2（插件预设）。四列是同一次播放模式录像，光线一样，镜头跟着人上下（`-roeView follow`，g05 技能 1 会飞起来）。

  和游戏手 K 差多少（每根骨在骨盆坐标系里的方向夹角，均方根，括号里是最大）：

  | | 我们的骨骼布料 | Magica Cloth 2（我们的参数） | Magica Cloth 2（插件预设） |
  |---|---|---|---|
  | g04 裙子 | 65.0°（177°） | 64.4°（177°）* | 62.3°（174°）* |
  | g04 头发 | **14.3°**（73°） | 25.7°（101°） | 32.7°（122°） |
  | g05 飘带 | **23.7°**（121°） | 28.7°（144°） | 32.3°（110°） |
  | g05 头发 | **13.3°**（77°） | 19.5°（101°） | 36.2°（129°） |

  \* g04 的三块裙片在 Magica 里是网格布（MeshCloth），动的是顶点，骨头停在基础姿势上，这两个数主要反映基础姿势，看视频准。

  看到的：
  - **g04 的裙子**：游戏里技能 1 跳起时裙片往两边甩开像扇子，技能 2 踢腿时前片被甩到身体一侧、再从身后扬起来；三种物理都让裙片往下垂，
    插件预设踢腿时还把前片堆在地上。弯腰收招（5.2 秒）时四列差不多。差距和 Inase 一样：在目标（我们照"自然下垂"调的），不在用哪个插件。
  - **g05 的飘带**：我们的和游戏最像（技能 2 横着甩的那一圈几乎一样），因为是贴着游戏关键帧模拟的；
    Magica 两种设置不看关键帧，飘带会被甩开或飘起来（技能 1 收招时一条飘到右边，插件预设技能 2 时两条往上翘）。
  - **头发**：我们的骨骼布料最接近，Magica 插件预设差得最多（甩得乱）。实际对打时游戏动作里头发直接用关键帧，所以这一行只在全程交给物理时才有意义。

- **结论**：现在的组合就是离游戏最近的。游戏自己的动作里，g04 的裙子和两人的头发用关键帧，和游戏一样；g05 的飘带用我们的模拟，最接近游戏。
  真正不一样的只有动捕动作（游戏里没有这些动作），在那里我们的裙子偏"垂"，游戏的手 K 偏"甩"。
  - 如果想让动捕动作里的裙子也甩起来像游戏，办法和 Inase 那次建议的一样：拿游戏关键帧当目标自动调物理参数，
    做成和现在的"自然下垂"可以切换的两套（等你定）。

- **工具**：
  - `tools/cloth_key_stats.py <id>`：游戏动作里每类布料骨甩多大（上面第一个表）；`skirt_key_models.load` 也认 `..._ld.ab` 这种不带 `_prelude` 的战斗包了。
  - 新的检查开关 `-roeMeasureKeys ribbon,hair`（`FighterRig.MeasureKeyKinds`）：除了裙子，这几类骨（按骨骼布料的命名规则）也在游戏动作里和关键帧比，
    每类单独报数；每条链最上面那根（锚点，不模拟）不算。
  - 播放模式里，量和关键帧的差等画面画出来时再量（`FighterRig.MeasureKeysLate`、`MeasureKeysNow`）：
    Magica Cloth 2 在战斗那一步之后（PreLateUpdate）才写骨头，之前在战斗那一步里量，量到的是 Magica 动之前的姿势（飘带两种设置都是 4.0°，是错的）。
  - 录像里一条可以单独保留游戏关键帧：设置后面加 `:keys`（`off:keys` 就是游戏的样子，当对比的第一列）。

```powershell
python tools\cloth_key_stats.py g04
python tools\cloth_key_stats.py g05
# 游戏手 K | 骨骼布料 | Magica（我们的参数）| Magica（插件预设），同一次播放模式录像（约 15 分钟）
.\tools\unity_batch.ps1 -NoQuit -Graphics -TimeoutMinutes 60 -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run -Name luf_vs_game `
    -Extra '-roeChars','g04,g05','-roeCloths','off:keys,magica_style,magica,magica_full','-roeScript','skills','-roeSkirtKeys','off','-roeMeasureKeys','ribbon,hair','-roeView','follow','-roeOut','_work\luf_vs_game'
python tools\takes_grid_video.py _work\luf_vs_game out\luf_cloth_vs_game.mp4 `
    --grid "g04_off_keys,g04_magica_style,g04_magica,g04_magica_full;g05_off_keys,g05_magica_style,g05_magica,g05_magica_full" `
    --titles "off_keys=游戏里（手 K 关键帧）,magica_style=我们的骨骼布料（现在用的）,magica=Magica Cloth 2（我们的参数）,magica_full=Magica Cloth 2（插件预设）"
```

### Luf 的裙子照游戏甩：动捕动作里也甩起来（10-11）

用户 10-11："做吧，让动捕动作里的裙子也甩起来"（接上一节：游戏里技能的裙子是甩出来的，我们的动捕动作里裙子偏"垂"）。

- **怎么调的**（`RoeSkirtTune`，编辑器批处理，不出图）：
  - 在她自己的技能 1、技能 2 里把裙子关键帧交给骨骼布料（和动捕动作一样：裙子跟着腿 + 下垂 + 物理），
    每一试量裙子和游戏手 K 差多少（每根裙骨在骨盆坐标系里的方向夹角，均方根）。
  - 坐标搜索：一次改一个参数，每个候选值试一遍，留下最好的，一共三轮（121 次，每次约 8 秒）。
    参数：下垂多少（`drape`）、拉回动画姿势的力（`restore`）、阻尼、跟着身体动的惯性（`inertia`、`worldInertia`）、
    转身的上限、角度上限（`limitRoot`、`limitTip`）、离动画姿势的最大距离、重力、`tether`。
  - 检验：用没参与调参的超必杀、受击、react_02 再量一次。
- **结果**（g04）：

  | | 原来（自然下垂） | 照游戏甩（第一版，下垂 0.3） | 照游戏甩（现在，下垂 1） |
  |---|---|---|---|
  | 技能 1、2（调参用的） | 65.1° | 58.2° | 62.8° |
  | 超必杀、受击、react_02（检验） | 43.6° | 40.9° | 42.9° |

  - 差距只缩小了一部分：游戏的裙子是美术按"甩"画出来的，很多帧不是物理能算出来的样子。
  - 方向很清楚，就是"更甩"：惯性 0.3 → 1（完全感受到自己的转身和踢腿）、拉回 0.35 → 0.1、
    裙尖角度上限 35° → 60°、离动画姿势的最大距离（根部）3 → 12 cm。重力留在原来的 5（搜索给的 1.5 只好 0.4°，但太轻时窄裙片碰到地面会往外卷，见"左边的裙子翘起来"）。
  - 第一版还把下垂从 1 降到 0.3（分数最好），结果裙子粘腿，改回 1，见下面"裙子粘腿"。下垂固定为 1 再搜一遍，其余参数没有更好的了。
  - 附近一大片参数分数都差不多（58.2–58.3°），所以取了其中看起来更自然的值。
- **动捕动作里的效果**（播放模式，`-roeScript moves`，左原来 / 右新的）：`out\g04_skirt_swing_front.mp4`（正面）、`out\g04_skirt_swing_backright.mp4`（右后，臀部特写）。
  - 走路时前片跟着迈步往一边摆，侧步、刺拳时裙片往身后、身侧飘开；高踢时前片被踢起来甩到一边（游戏里的技能就是这样）；
    原来的几乎一直垂着，高踢时腿从垂着的前片旁边穿过去。
  - 量的数（整段，现在这版：下垂 1、重力 5）：穿进身体 0.47% → 0.42%，抖动 9.1 → 8.8 毫米/步（最坏的一步 162 → 104），
    网格拉伸平均 2.5% → 2.6%（最坏的一步 67% → 79%）。
- **怎么用**：
  - F4 列表多了一个方案 `swing`（"骨骼布料，裙子照游戏手 K 甩"）：裙子用这个人调好的参数，其余（头发、胸、链子）和 `magica_style` 一样。
  - F4 默认的"各用自己游戏的"（`auto`）：调过的人（现在是 g04）自动用 `swing`；想要原来的"自然下垂"，F4 切到 `magica_style`。
  - F5 照旧在"下垂 / 跟腿走"之间切。
  - 游戏自己的动作（技能、受击、开场、胜利、选人展示）照旧用游戏的裙子关键帧，不受影响。
  - Inase 在 exe 里本来就是 Magica Cloth 2 全接管（插件预设），裙子已经会甩，这次没有动她。
- **参数**（`RoeBoneCloth.SwingTunings`，默认值写在代码里）：
  `g04 = restore=0.1,inertia=1,worldInertia=0.75,limitTip=60,maxDistanceRoot=0.12`（另可写 `drape=` 下垂的比例、`gravity=` 重力，默认 1、5）。
  可以覆盖（exe 也认）：`ROEFighter.exe -roeSkirtSwing "g04:restore=0.2,gravity=3"`；给别的角色加：`"a08:..."`；空的一条把这个人去掉。

```powershell
# 调参（约 15 分钟）：trials.txt 每一试，best.txt 最好的一组；-roeSkip drape 把下垂固定在 -roeStart 给的值
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeSkirtTune.Run -Graphics -TimeoutMinutes 90 -Extra '-roeChar','g04','-roePasses','2','-roeSkip','drape','-roeStart','drape=1,restore=0.1,inertia=1,worldInertia=0.75,limitTip=60,maxDistanceRoot=0.12,gravity=1.5','-roeOut','_work\skirt_tune_g04_drape1'
# 核对：技能里新旧和游戏手 K 差多少（日志 "skirt against the game's keys"）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','g04','-roeCloths','magica_style,swing','-roeScript','skills','-roeSkirtKeys','off','-roeOut','_work\swing_check'
# 动捕动作里新旧对比（播放模式，每个视角约 7 分钟），-roeView backright 是右后
.\tools\unity_batch.ps1 -NoQuit -Graphics -TimeoutMinutes 50 -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run -Name swing_moves_front `
    -Extra '-roeChars','g04','-roeCloths','magica_style,swing','-roeScript','moves','-roeView','front','-roeOut','_work\swing_moves_front'
python tools\cloth_demo_video.py _work\swing_moves_front out\g04_skirt_swing_front.mp4 --chars g04 --variants magica_style,swing
```

### 裙子粘腿（10-11）

用户 10-11："luf的腿部权重是否有问题，似乎和裙子有粘连"。

- **查了什么**（`RoeWeightProbe.SkirtLegs`）：她所有网格逐个材质数，哪些顶点挂在裙子骨上、哪些挂在腿骨上（大腿、小腿、脚、扭转骨、膝盖和臀部辅助骨）、哪些两边都挂；
  每个顶点（绑定位置、骨骼和权重）写到 `_work\weights\<id>.tsv`。
  - 裙子网格基本干净：两边都挂的只有腰带一圈（离地 1.00–1.07 米，裙子缝在腰上的地方，正常）。
  - **裸体身体**（爆衣用，垫在衣服下面）有 **6 个大腿正面的顶点（离地 0.83 米）100% 挂在裙子骨上**：裙子一甩，就把大腿皮肤扯过去。
    这不是游戏的权重：裸体的权重是我们抄的（`RoeNudeBody`：抄贴在它上面的东西）。游戏在那里把皮肤剪掉了，上面贴着一块裙片，这几个点就抄了裙片的。
  - **裙子的动画姿势**（`RoeSkirtRig`）：动捕动作里裙骨跟着腿走，权重是照游戏手 K 拟合的——前片中段 55–65% 挂在右大腿上，左片下段 40–50% 挂左小腿，
    后片下段 33–50% 挂右小腿。原来的"自然下垂"（下垂 1）把方向换成竖直往下，腿只在碰撞时推裙子；
    今天第一版"swing"只下垂 0.3，前片 70% 跟着大腿走，一踢腿前片就被大腿带起来，像粘在腿上。
- **改了什么**：
  - 裸体抄权重时，会飘的东西（裙子、飘带、头发、链子的骨，按骨骼布料的命名规则）一律不给权重；抄完什么都不剩的点改抄最近的皮肤。
    重建了所有角色的爆衣身体：g04 9 个点、m02 4 个、eve37 1 个改了，其余角色没有这种点。g04 那 6 个点现在挂在大腿上（97%）和骨盆上。
  - "swing"改回完全下垂（下垂 1），其余让裙子甩的参数保留（下垂固定为 1 重新调了一遍，没有更好的）。和游戏手 K 比 62.8°（原来 65.1°）；
    动捕动作里穿进身体、抖动都比原来的少一点（见上一节）。踢腿时前片不再跟着大腿上去，在腿下面、后面垂着、甩着。
- **踩过的坑**：重建爆衣（`RoeBurstBuilder.Check`）之后**要重建对战场景**（`RoeFightScene.Build`）：裸体身体去掉了裙子骨，骨骼的编号变了，
  场景里还留着旧的骨骼列表，权重就对错了骨头（量出来 3,000 个顶点"挂在裙子骨上"，重建场景后是 0）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeWeightProbe.SkirtLegs -Extra '-roeChar','g04'       # → _work\weights\g04.tsv
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','a08,g04,b10,g05,m02,eve09,eve37,fio005'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics        # 爆衣重建之后一定要跟着做
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
```

### 左边的裙子翘起来（10-11）

用户 10-11："luf的左边的裙子为什么翘起来了一点，ROE游戏中就是这样的么"。

- **游戏里**（她自己的关键帧，`tools/cloth_key_stats.py` 的做法，每根骨离竖直往下多少度）：战斗站姿 `idle_01` 里左裙片从腰上往外张开 51°，
  往下 25°，中段几乎竖直（9–11°），**下摆又往外翘 17–33°**（晃起来最多 42°）。展示站姿 `idle_02` 下摆翘得少（9–12°）。
  所以游戏里左裙片的下摆本来就往外翘一点。
- **我们这里**（`RoeFightProbe.SkirtHang`，对打架势、骨骼布料）：左裙片 8 根骨 90° / 34° / 2° / 2° / 1° / 1° / **69° / 90°**。
  - 对打架势是不知火舞的（10-03 起），蹲得比游戏的站姿低，Luf 的裙片长到脚面，**下摆碰到地面**，地面碰撞把最后两根骨顶平；
    关掉地面碰撞（`-roeSkirt floor=0`）就都竖直（1°）。原来的"自然下垂"也是这样，只是看不太出来。
  - 今天的"swing"重力只有 1.5，那条窄裙片碰到地面后不往下落，从脚边往外卷成 S 形——看起来就是"左边的裙子翘起来"。
    重力改回 5（和游戏手 K 的差距 62.8° → 62.5°，几乎不变）：窄裙片竖直垂到地面，和原来一样。
  - 另外，蹲着的时候左裙片最上面一根骨在胯部是水平往外的（90°，游戏站姿 51°）——下一节改成和游戏一样了。
- **对比图** `out\g04_left_panel.jpg`（从她左边看全身）：swing 重力 1.5（卷起来）/ swing 重力 5（现在）/ 原来的自然下垂 / 游戏的战斗站姿。
- **工具**：
  - 演示脚本 `-roeScript stance`：前 3 秒是对打架势（裙子交给物理），后 3 秒播游戏的战斗站姿 `idle_01`（裙子用游戏的关键帧）；
    为此 `idle_01` 也作为游戏动作放进了角色（`RoeFightScene.GameClips`），要重建场景。
  - 新视角 `-roeView left` / `right`：从她这一侧看全身，镜头低一点，看得到地上的下摆。
  - 骨骼布料的调试参数多了 `floor`（`-roeSkirt floor=0` 关掉裙子的地面碰撞，只用来查问题）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics        # idle_01 作为游戏动作
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','g04','-roeCloths','swing,magica_style','-roeScript','stance','-roeView','left','-roeOut','_work\stance_left'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtHang -Graphics -Extra '-roeDrapes','1','-roeChains'                       # 每根裙骨的角度和离皮肤多远
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtHang -Graphics -Extra '-roeDrapes','1','-roeChains','-roeSkirt','floor=0'  # 关掉地面碰撞再看
```

### 胯部那一段和游戏一样（10-11）

用户 10-11："luf胯部那一段也改成和游戏一样吧"。

- **原来**："自然下垂"把每根裙骨都拉成竖直往下，再把穿进皮肤的推出来。蹲着的对打架势里，左裙片最上面一根骨被推成水平往外（91°），
  前片腰上那一段又被拉成笔直往下（11°）；游戏站姿里这两处分别是 51° 和 46°（斜着搭在胯上、肚子上）。
- **现在**（`RoeSkirtRig.DrapeByLevel`，默认 `{0, 0}`）：每片裙子最上面两根骨不下垂，用照游戏站姿拟合的形状（跟着胯和腿，和游戏手 K 一样），
  也不再被推出皮肤（拟合的形状本来就离皮肤 1–2 厘米）；从第三根骨往下照旧下垂，所以踢腿时下面那一大段不会又被大腿带起来。
  - 试过第二根骨下垂一半：推出皮肤那一步把前片的第二根骨顶到 66°，比全用拟合的（35°）差，没用。
- **对比**（`RoeFightProbe.SkirtHang`，对打架势，每片裙子最上面两根骨离竖直往下多少度）：

  | 裙片 | 游戏的战斗站姿 | 原来 | 现在 |
  |---|---|---|---|
  | 左裙片 | 51° / 25° | 91° / 33° | 58° / 40° |
  | 前片 | 47° / 17° | 11° / 1° | 59° / 35° |
  | 另一片前片 | 46° / 17° | 10° / 1° | 39° / 27° |
  | 装饰线 | 50° / 44° | 87° / 6° | 41° / 28° |
  | 后片 | 63° / 17° | 48° / 7° | 45° / 8° |

- 对比图 `out\g04_hip_segment.jpg`：上排从左边看全身，下排左前方看胯部；每排是改之前 / 改之后 / 游戏的战斗站姿。左边那条窄裙片在胯部不再先水平伸出去。
- 量的数：和游戏手 K 的差距 62.5° → 61.9°；动捕动作里穿进身体 0.42% → 0.53%（胯部的骨贴着皮肤，偶尔进去一点；看不出来），
  抖动 8.8 → 9.2 毫米/步（最坏的一步 104 → 94），网格拉伸 2.6% → 2.5%。踢腿时前片仍然在腿下面垂着，没有粘回去。
- 这条对所有用骨骼布料的裙子都起作用（g04；a08 在 exe 里是 Magica Cloth 2 全接管，不受影响）。可以改：`RoeFightProbe.SkirtHang -roeDrapeByLevel 0,0.5` 试别的比例。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.SkirtHang -Graphics -Extra '-roeDrapes','1','-roeChains','-roeDrapeByLevel','0,0'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','g04','-roeCloths','swing,magica_style','-roeScript','stance','-roeView','left','-roeOut','_work\stance_hip_left'
```

### Luf 站游戏里的姿势；技能键 1、2、3（10-11）

用户 10-11："luf还原ROE里的姿势吧，战斗按钮123，用luf在ROE里的三个招数"。

- **站架**：Luf（g04）从 10-03 起站的是不知火舞（DOA6）的格斗架势——两腿岔开、蹲低、身体前倾，和她在 ROE 里差很多。
  现在出招之间她站的就是游戏里的战斗站姿 `idle_01`（1.33 秒一个循环，手搭在胯上，直身站着）。
  - 做法：`idle_01` 早就转成了人形动作（`Generated\g04\humanoid\g04_idle_01.anim`，0 层一直在放它）。场景构建时只取它的人体曲线（肌肉和根，
    `RoeHumanoidClips.HumanOnly`），存成 `g04_guard_stance.anim`，放进动捕层当她的 `guard`（`FighterRig.ownGuard`，`UseMotions` 换上，F3 换动作包也不变）。
  - 扇子、头发、裙子的曲线故意不放进去：动捕层里一个片段有这些骨头、另一个没有，混合时没有的那一方会把它们拉回绑定姿势
    （"踩过的坑"第 1 条：出拳时扇子张成巨大的扇面）。这些骨头仍由 0 层的 `idle_01` 和布料管，辅助骨照旧跟着四肢走。
  - 前进、后退、侧步、四个普通攻击（不知火舞的冲步刺拳、肘击、扑身冲拳、高踢）都没变，出招从游戏站姿直接接过去，收招回到游戏站姿。
    侧步时上半身是站架的，所以现在侧步也是直身的。
  - 身体姿势和游戏的 `idle_01` 一样（对比图第二、三列）。裙子不一样的地方：站着时裙子是"swing"布料模拟的，不是游戏的手 K——
    游戏里左边那条窄裙片的下摆往外翘一点，我们的直着垂下。（当晚改了：站着时直接放游戏的手 K，见下一节。）
  - 场景构建的开关 `-roeGuard`（默认 `g04=stance`；10-17 起是 `g04=stance,a08=stance`，见"Inase 站游戏里的姿势"一节）：`g04=pack` 回到不知火舞的架势；别的角色也能加，例如 `g04=stance,b10=stance`。
    g05（女神 Luf）没改：她游戏里的战斗站姿浮在半空 23 厘米，`rig.stance` 用的是她的展示待机 `idle_02`。
- **技能键 1、2、3**：主键盘上的数字键 1、2、3 就是技能 1、技能 2、超必杀（`PlayerInput`），对每个 ROE 角色都是她游戏里的
  `skill_01`、`skill_02`、`skill_03`（技能一直是这三个，见"天野（m02）加入战斗"一节；原来只能按 L、O、P 或搓招）。L、O、P、搓招、手柄肩键照旧能用。
  - 规则没改：超必杀要满能量槽；技能 1、2 不耗能量；对手在 6 米（超必杀 7 米）以内才放得出来。屏幕下方的按键提示也改了。
  - 2P 照旧是小键盘 3、6、9。
- **对比视频** `out\g04_roe_stance.mp4`（30 秒，`RoeClothDemo.Guards`，左：原来不知火舞的站架；右：现在的游戏站姿）：
  同一套按键脚本——站架、前进、后退、两边侧步、A、C、D、B 四个普通攻击，然后按 1、2、3 放三个技能（脚本 `all`，放技能前把能量槽加满）。
  字幕写着每一步按的键。
- **对比图** `out\g04_roe_stance.jpg`：上排正面、下排左侧；每排是原来的站架 / 现在的站架 / 游戏的 `idle_01`（裙子是游戏手 K）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics [-Extra '-roeGuard','g04=stance']
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Guards -Graphics -Extra '-roeChars','g04','-roeScript','all','-roeView','front','-roeOut','_work\guard_all_front'
python tools\cloth_demo_video.py _work\guard_all_front out\g04_roe_stance.mp4 --variants guard_pack,guard_stance
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Guards -Graphics -Extra '-roeChars','g04','-roeScript','stance','-roeView','left','-roeOut','_work\guard_stance_left'
```

### Luf 的裙子：重新从 ROE 导出，完全照 ROE 再看（10-11）

用户 10-11："luf的裙子能像ROE一样自然下垂么，要不重新导出ROE中的看一下吧，可能改了太多次改乱了，就完全遵从ROE本身的物理的角度再看看"。

- **重新导出、核对**：
  - Luf 的 51 个游戏包重新拷出来，用 AssetRipper 重新还原到 `_work\ripped_g04_fresh`。
  - 和工程里用的 `Assets\ROE\g04` 逐个文件比：Luf 自己的 18 个包、112 个文件（网格、骨骼、动作、材质、贴图、预制体）全部一样，只有 Unity 的内部编号不同。
  - 游戏包本身最后更新在 2026-08（战斗低模 2025-12），我们 10 月第一次导出之后没变过。
  - 转换后的游戏动作和原版逐骨回放对比（`RoeHumanoidClips`）：待机 `idle_01` 平均差 0.01 厘米，三个技能 0.02–0.08 厘米。
    游戏动作里裙子就是 ROE 的手 K，原始数据和转换都没有改乱。
  - 和游戏不一样的只有游戏没有的部分：站架和动捕动作里，裙子是我们模拟的，而且叠了五层：跟腿走的拟合、自然下垂、上两根骨保留拟合、胯部对齐、swing 调参。
- **ROE 本身的物理**（`tools\roe_mc2_dump.py` 从新导出的大厅预制体 `meta_armor_pc_g04_hd` 读出来）：
  - 裙子没有任何物理，战斗、展示、大厅里全是手 K。
  - Luf 身上只有一个 Magica Cloth 2 组件，在大厅模型上，管 6 条头发（左右刘海、4 条后发）。参数：连接方式 Line（每条链单独算），
    重力 5、阻尼 0.05、角度恢复 0、角度限制 90°（根部 0°，越往下越大）、惯性全 0、粒子最快 1 米/秒、粒子半径 1.5 厘米、
    7 个胶囊碰撞体（胸、头、颈、上身），基准是初始姿势。和 10-05 调研的结论一样：ROE 所有布料都用这一套"头发"参数。
- **新的物理方案 `roe`**（ROE 自己的，F4 能切到；`RoeMagicaCloth` 的 `game` 模式）：
  - 游戏动作和站架里（站架现在就是游戏的待机），所有部位直接放 ROE 的手 K。
  - 动起来时：
    - 头发：用游戏的那个组件，原样照搬（同样的参数、7 个碰撞体、6 条链）。`ImportJson` 不导入的几项（旋转插值、根部旋转、
      基准姿势比例等，游戏是 1、1、0）另外照游戏补上，Magica 默认的旋转插值和根部旋转是 0.5。
    - 裙子、腰链：用同一套参数，每条链一个 Line，加上我们的腿部胶囊。游戏腰以下没有碰撞体，因为它的裙子不模拟。
    - 胸：和 ROE 一样不加物理。
  - 我们的东西只留了一样：裙子跟着腿走的姿势（`RoeSkirtRig`，本身就是从 ROE 手 K 拟合的），Magica 以它为基准。
    原因：照游戏用初始姿势当基准、根部角度限制又是 0° 的话，腿一动，裙子上半段就像木板一样从胯上支出去（试过）。
  - 结果：站着和游戏一模一样；但走路、侧步、出拳时，这套"头发参数"放在拖地长裙上显得飘。后片被脚后跟踢起来，要横在身后好一会儿才落下，
    因为每秒 1 米的限速让它落得慢。去掉限速也试过：阻尼只有 0.05，裙片像旗子一样甩出去，更差。走路、后退时穿进腿也多。
- **所以默认改成**：
  - 站在游戏站姿里（站架）时，不管哪种物理方案，都直接放 ROE 的手 K，和技能、开场、胜利一样：裙子不跟腿走、不按胯对齐，布料不写。
  - 一动起来（走、退、出拳）或侧步迈腿，布料再接手，Luf 默认还是 swing。切换按站架动作的混合权重渐变；布料一直在后台算，所以不会跳。
  - `FighterRig`：站架和 0 层的游戏待机逐帧对齐，身体和关键帧里的裙子、头发是同一帧。
- **对比**（`RoeClothPlayDemo`，同一套操作：站架、前进、后退、侧步、四个普通攻击，再按 1、2、3 放技能。每格是"穿进身体 % / 抖动 毫米"）：

  | 段 | 原来（站着也模拟） | 现在（站着用手 K） | 完全照 ROE |
  |---|---|---|---|
  | 站架 | 0.25 / 1.2 | 0 / 0.5 | 0 / 0.5 |
  | 前进 | 0.09 / 5.6 | 0.09 / 5.5 | 0.49 / 3.5 |
  | 后退 | 0.14 / 5.1 | 0.14 / 5.1 | 0.67 / 2.9 |
  | 侧步 | 0.20 / 2.9 | 0.19 / 3.0 | 0.38 / 5.0 |
  | 四个普通攻击 | 0.23–0.42 | 0.12–0.32 | 0.25–0.77 |
  | 三个技能 | 0.13 / 0.26 / 0.34 | 0.03 / 0.12 / 0.29 | 0.03 / 0.12 / 0.29 |
  | 全程 | 0.24 / 6.3 | 0.15 / 5.8 | 0.24 / 5.5 |

  技能本身三列都是游戏手 K，差别在技能前后站着的那段。
- 视频 `out\g04_skirt_roe.mp4`（30 秒，左：原来；中：现在；右：完全照 ROE）。图 `out\g04_skirt_roe.jpg`：站架、前进、侧步、出拳后回到站架，四个时刻各一行。
- 想看 ROE 方案：比赛里按 F4 切到 "roe"（exe 里装着 Magica Cloth 2）。去掉限速的那条：`-roeCloths 'roe:speedLimit=0'`。
- 游戏的组件数据写在 `Assets\ROE\<角色>\magica_lobby.json`（游戏数据，不进仓库），场景构建时读进 `FighterRig.gameMagica`；
  别的 ROE 角色先跑一遍 `roe_mc2_dump.py --fighter <id>` 就能用 `roe`。没读的话，`roe` 照这套参数手写一份（`RoeMagicaCloth.RoeTemplate`）。

```powershell
python tools\stage_bundles.py _work\bundles_g04_fresh g04
python tools\rip.py --bundles _work\bundles_g04_fresh --out _work\ripped_g04_fresh --log _work\assetripper.log
python tools\roe_mc2_dump.py "_work\ripped_g04_fresh\ExportedProject\Assets\AssetBundles\meta_armor_pc_g04_hd & ld\pc_g04_hd.prefab" --fighter g04
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run -Graphics -NoQuit -Extra '-roeChars','g04','-roeCloths','swing,roe','-roeScript','all','-roeView','front','-roeOut','_work\roe_cloth_front5'
# 视频的三栏：原来那一栏是改之前录的 swing（_work\roe_cloth_front2\g04_swing），和这两条放进 _work\roe_cloth_compare（takes.txt 里叫 swing_old）
python tools\cloth_demo_video.py _work\roe_cloth_compare out\g04_skirt_roe.mp4 --variants swing_old,swing,roe
```

### Kart 最新的一套（b14，鹤袍）加入战斗（10-11）

用户 10-11："把kart 最新的模型加入到游戏中"。

- **b14 = Kart 的第 14 套**（游戏单位 `suit_kart_14`）：游戏包是 2026-09-12 的，Kart 其他十几套都在 8 月或更早，所以是最新的一套。
  - 样子：白蓝长袍只盖右肩，右臂一条拖到膝下的纱袖，腰带上停着一只丹顶鹤，裙片下摆是羽毛；金冠、耳坠、项圈，黑色细高跟。
  - 武器是一把长剑，剑柄挂着长穗；站架里右手握着剑。
  - 游戏里还有一个粉白换色款（`outfit1`），这次没加。
  - b10 保留。b14 的显示名是 CRANE KART，两个 Kart 同场时分得清（照 GODDESS LUF 的先例）；选人卡片上的词是 Crane。
  - 家族裸体底模和 b10 一样是 b01；名单默认加在 m02 后面。
- **三个技能**：游戏的 `skill_01`–`03`，特效、音效、语音都是游戏的。三个都打中（`out\b14_skills.mp4`，对 Inase）：

  | 技能 | 怎么打 | 能打多远 | 打中掉血 |
  |---|---|---|---|
  | 技能 1 | 冲到对手面前，挥剑两下 | 冲刺后 2.9 米 | 150 |
  | 技能 2 | 原地刺剑，命中特效在她前方 4.9 米 | 5.9 米 | 190 |
  | 超必杀 | 天上落下一排羽剑，击倒 | 8.0 米（命中特效在 7.0 米） | 360，对手掉一段衣服 |

- **普通攻击**：先用动作包的四招，和 m02 一样。剑只在技能、开场、胜利时出现。要像 Inase、b10 那样平时拿剑打，另外做。
- **选人时的展示动作和台词**：`python tools\roe_showcase.py b14`。
  - 顺带发现两个问题：m02 加入时漏了这一步；b14 和 m02 的游戏把 `react` 拼成了 `recat`，选人确认时找不到这段动作。
  - 现在 `roe_showcase.py` 按 `import_ripped.py` 的拼写表改名（`CLIP_ALIASES`），m02 也补上了。
- **布料**：
  - 裙片 10 条链（68 根骨）、头发、胸，照旧。
  - **右边的长纱袖**（`Sleeve_R1`，11 节，挂在右前臂的扭转骨上）：
    - 按名字认作"飘带"，参数单独一套（`RoeBoneCloth.Sleeve`）：不往动画姿势拉，只有袖根贴着前臂（袖根 25°，到袖尖放宽到 150°），靠重力垂下来；
    - 技能、受击、开场、胜利这些游戏动作里照游戏手 K（别的飘带在游戏动作里也模拟，袖子不）。
    - 第一版直接用飘带的参数（往站姿拉 0.3、限制 15° / 50°），站架里袖子垂在放下的手臂旁，一抬手防御、出拳，整片袖子就像木板一样立在头顶。
    - 辅助骨拟合（`RoeHelperFit`）不再把袖子的骨当成跟着前臂走的辅助骨。
  - 腰带上两条流苏（`Tassel_L1` / `R1`）当链子模拟。
  - 她游戏大厅的 Magica Cloth 2 只管头发（`roe_mc2_dump.py --fighter b14` 读好了，F4 的 `roe` 能用）。
- **爆衣四段**（`Editor\Burst\b14.json`，检查图 `out\clothes_burst\unity\b14_sheet.png`）：

  | 段 | 掉什么 |
  |---|---|
  | 1 | 金冠、耳坠、项圈和吊坠、左胸的金饰、右肩的肩饰 |
  | 2 | 腰上的丹顶鹤、流苏和珠串、右边的长纱袖 |
  | 3 | 左臂的袖套、戒指、高跟鞋 |
  | 4 | 长袍（上身、腰带、裙片是连着的一件）和金色丁字裤 |

  - 长袍、纱袖、袖子衬里三种材质在接缝处共用顶点。写成一条 outfit 会焊成一整块，所以每种材质写一条，纱袖才能单独掉。
    长袍本身的上身和裙片连在一起，只能一起掉。
  - **新加的规则词 `ownWeights`**（`RoeNudeBody`）：
    - 问题：她的套装在右上臂没有皮肤（被长袍盖住，游戏裁掉了）。盖在上面的纱袖在游戏里挂在前臂扭转骨上，肩饰挂在自己的骨头上。
      裸体照旧从盖着的东西抄权重，上臂就跟着前臂和肩饰走，站架里整条上臂横着支出去，像一块木板。
    - 现在写 `"ownWeights": "^Bip001 R UpperArm$"`：底模自己挂在右上臂上的那些顶点，只要套装皮肤够不着（1.5 厘米外），就用底模 b01 自己的权重。
      骨头按名字找，套装没有的辅助骨（比如 b01 的 `ArmTwist_R`）归到最近的祖先。
    - 套装网格只挂扭转骨、不挂上臂本身，这些套装没蒙皮的骨头按底模的绑定姿势补上（同一家族的骨架，站的位置一样）。
    - 试过、没用的：
      - 全身都用底模权重：大腿上和套装皮肤的权重交界处出折痕（套装的大腿扭转骨挂在脊柱上，底模的挂在大腿上）；
      - 肩膀也算进去：肩顶多一道折痕；
      - Eve 那样"被衣服盖着的身体等衣服掉了再画"（`reveal`）：胸口长袍边缘露出黑洞。
    - 剩下肩顶一个很小的台阶，只在全身那一段能看到。
  - 她的皮肤和 b01 的身体是同一个曲面，只是做得粗一些：沿法线中位差 0 毫米，5% / 95% 是 −2 / +1 毫米。
  - 爆衣演示（Inase 打她，两局四段全掉）：`out\clothes_burst_demo_b14.mp4`。
- 电脑对电脑（对 b10，60 秒）：`out\fight_cpu_match_b14.mp4`。

```powershell
# 1. 拷包、还原、导入（AssetRipper 服务开着，见"从零重做一遍"；b01 的裸体底模工程里已有）
python tools\stage_bundles.py _work\bundles_b14 b14
python tools\rip.py --bundles _work\bundles_b14 --out _work\ripped_b14 --log _work\assetripper.log
python tools\import_ripped.py --src _work\ripped_b14\ExportedProject\Assets
# 2. 技能、选人的展示动作、游戏大厅的物理
copy "<游戏包目录>\gameplay_prefab_suit_kart_14.ab" _work\bundles_skills_b14\
python tools\rip.py --bundles _work\bundles_skills_b14 --out _work\ripped_skills_b14 --log _work\assetripper.log
python tools\import_skills.py suit_kart_14 --src _work\ripped_skills_b14\ExportedProject\Assets
python tools\skill_sheet.py suit_kart_14 b14
python tools\roe_showcase.py b14 m02
python tools\roe_mc2_dump.py "Assets\ROE\b14\meta_armor_pc_b14_hd_ld\pc_b14_hd.prefab" --fighter b14
# 3. Unity
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFighterBuilder.BuildAll -Extra '-roeChars','b14'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHumanoidClips.ConvertAll -Extra '-roeChars','b14'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.Run -Extra '-roeChars','b14'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.FitSkirt -Extra '-roeChars','b14'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','b14'
python tools\burst_sheet.py --chars b14
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
# 录像：技能、爆衣、电脑对电脑
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.SkillReel -Graphics -Extra '-roeP1','b14','-roeP2','a08'
python tools\skill_reel_video.py out\b14_skills.mp4 _work\skills_b14
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','b14','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_b14'
python tools\burst_video.py _work\burst_demo_b14 out\clothes_burst_demo_b14.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','b14','-roeP2','b10','-roeSeconds','60','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_b14'
python tools\make_video.py _work\fight_b14 out\fight_cpu_match_b14.mp4
# 她每个顶点的权重（_work\weights\b14.tsv），右上臂的问题就是这样查出来的
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeWeightProbe.SkirtLegs -Graphics -Extra '-roeChar','b14'
```

### Crane Kart（b14）拿剑打普通攻击；右肩照左肩取权重（10-17）

用户 10-17："继续"。接着做 10-11 留下的两件事：b14 平时拿剑打普通攻击（以前用动作包的拳脚，剑只在技能里出现），全裸那段右肩顶的小台阶。

**她的剑**

- 一把长剑，全长 1.43 米，从手到剑尖 1.16 米，剑柄末端挂一条短穗。
- 剑身挂在 `Wp_01` 上，`Wp_01` 在 `Bip001 Prop1` 下面，穗子是 `Wp_02..04`。这几根骨只有战斗低模有，10-11 已经接到高模上。
- 她游戏的站架 `idle_01` 是反手握：剑身贴着右前臂朝上，剑尖高过头顶。
- 三个技能里每次挥剑前，剑都在手里翻转约 160°，换成正手握（剑从虎口一侧伸出）。
- 所以握法取她技能 1 第 18–48 帧（两下挥砍）里的正手握。这段里剑在右手中最多晃 1.5 厘米、14°，结果在 `tools/roe/b14_grip.json`。
- 检查图 `out\weapon_grip\b14_0..2.png`：游戏站架原样、换成正手握、抬起手臂。
- `tools/roe_clips.py` 顺带改了：解码时先把战斗低模独有的骨（她的 `Bip001 Prop1`、`Wp_01..04`）接到高模骨架上，做法和 Unity 里的 `RoeFighterBuilder.GraftBone` 一样。以前这几根骨的关键帧被丢掉，量不了握法。

**挑招**：16 个候选（`tools/motionpacks/b14_sword_candidates.json`）

- 她自己三个技能里截的 7 段，加上 Fiona、Inase、b10 都还没用过的 9 个 Vindictus 单手剑招。
- 视频 `out\b14_sword_candidates.mp4`：每招先实速、再半速。
- 总览图 `out\b14_sword_candidates_sheet.jpg`：对战里按键后每 0.1 秒一格，红条是能打中的时间。
- 影棚检查图 `out\b14_sword_candidates_moves_sheet.jpg`：每招从头到尾 8 格。

**默认四招**：`tools/motionpacks/b14_sword.json`（`strikesOnly` + `grip`）

| 键 | 招式 | 出处 | 出手 | 够到 | 段 |
|---|---|---|---|---|---|
| A | 两连斩 | 她技能 1 的 0.6–1.3 秒 | 0.05 秒 | 2.15 米 | 中段 |
| B | 跃起斩 | 她技能 1 的 0.2–0.8 秒 | 0.05 秒 | 2.23 米 | 下段 |
| C | 回身刺 | 她技能 2 的 1.15–2.0 秒 | 0.53 秒 | 1.82 米 | 上段 |
| D | 翻身斩（击倒） | Vindictus 重击 3b（Attack_Strong03b），1.2 倍速 | 0.77 秒 | 2.22 米 | 中段 |

- 三招用她自己的动作：她是一身鹤袍、拖着长纱袖的剑舞，Vindictus 的重剑招大多是深蹲、弓步、往前冲好几米，放在她身上不像她。
- A 是技能 1 冲到对手面前之后的两刀，判定取第一刀（剑尖在身前 2.15 米、每秒 76 米），最快。
- B 是技能 1 起跳的那一段：起跳时剑尖贴着地面往前扫，所以是下段，要蹲着防。
- C 是技能 2 里转身、剑从头顶挥过去接一记前刺，剑尖停在胸口的高度：上段，蹲下能躲过去。
- D 是 Vindictus 的重击 3b：腾空横着翻一圈，落地横斩。三个人都没用过这招。1.2 倍速，出手 0.77 秒，是四招里最慢的。
- 能打中的时间（`hit`）按剑尖定（`RoeMotionPacks.Import -roeTips` 写出的每帧剑尖位置）：取剑尖往前扫得最远、最快的那几帧。整段自动量不准：1.2 米的剑，几乎每帧剑尖都超过每秒 5 米。
- 没选的：
  - 她技能 1 的劈下接低扫：剑在身侧、身后挥，只够到 0.53 米。
  - 技能 2 收尾的低扫：其实是慢慢收剑。
  - 超必杀开头的升空斩：停在半空。
  - Vindictus 的技能 4、5：十几米的冲刺、11 秒的长连段。
  - 重击 1b、2b、技能 2b：往前冲出去 3–5 米。
- 建场景时默认给 b14 这套（`-roeOwnStrikes` 的默认值加了 `b14=b14_sword`）；换回拳脚就在表里去掉这一项。

- 视频 `out\b14_sword_strikes.mp4`：四招，每招先实速再半速。
- 总览图 `out\b14_sword_moves_sheet.jpg`：对战里拍的，按键后每 0.1 秒一格。
- 检查图 `out\b14_sword_check_sheet.jpg`：影棚里每招 10 格。
- 电脑对电脑（对 Inase，120 秒）：`out\fight_cpu_match_b14_sword.mp4`。

**右肩顶的台阶**

- 样子：全身那一段，她右肩顶有一道棱。手臂放下时，三角肌跟着手臂往下走，肩顶那一圈不动，挤出一个折角（对比图 `out\b14_shoulder_fix.jpg`）。
- 不是右上臂权重的问题（10-11 那次修的是上臂）。按"底模挂在上臂上的比例"让两套权重平滑过渡、把锁骨也算进去，都没有用。
- 原因：
  - 她的套装皮肤在右肩被长袍盖着，游戏看不见，这块皮肤一直延伸到肩关节，却只挂在 Spine2 和锁骨上，没挂上臂。
  - 裸体离套装皮肤 1.5 厘米内就照抄它的权重，所以肩关节旁的皮肤不跟手臂走。
  - 再往外没有套装皮肤的地方，裸体以前抄的是金色肩饰的权重：肩饰挂在它自己的一根骨上，像一块硬甲。
- 修法：她左肩在游戏里是露出来的，权重做得认真，还用到了套装自己的扭转骨、肩部辅助骨（战斗里由 `RoeHelperFit` 驱动）。右肩就照左肩镜像取权重。
- 新加的规则词（`RoeNudeBody`，b14 的 `Editor\Burst\b14.json`）：
  - `ownOverSkin`：`ownWeights` 的区域里，有套装皮肤的地方也不照抄套装皮肤。
  - `ownMirror`：这个区域的权重不用底模自己的，用对面套装皮肤上的，骨头名左右对调（`Bip001 L ..` 换成 `Bip001 R ..`，`.._L` 换成 `.._R`）。
    - 镜像面是两根锁骨正中间、垂直于两根锁骨连线的平面。
    - 对面 2 厘米内没有皮肤时（左臂袖套底下游戏裁掉了皮肤），取贴在那里的衣物（袖套）的权重。都没有就用底模自己的。
  - 区域边缘按底模挂在这些骨上的比例过渡：25% 以下照旧，75% 以上全用新的，中间按比例混合。以前是一刀切：最大的那份权重在不在这些骨上。
  - b14 现在写的是 `"ownWeights": "^Bip001 R (UpperArm|Clavicle)$"`、`"ownOverSkin": true`、`"ownMirror": true`。
  - 别的角色不受影响：只有写了 `ownWeights` 的角色才走这些规则，现在只有 b14。
- 对比图 `out\b14_shoulder_fix.jpg`（全身那段的胸口特写，左边改之前，右边现在）。爆衣演示重录了：`out\clothes_burst_demo_b14.mp4`。
- 日志里这一行写着各有多少顶点：`mirrored from her other side: 752 (268 from a piece lying there) ... 811 places smoothed 8 times`。
- 检查的办法：`RoeBurstBuilder.Check` 加 `-roeFaces 1`，每一段另拍一张胸口特写（`<id>_stage<n>_chest.png`）。这次看不清时就是靠它看出来的：
  不是缺了一块（三角面正反图里肩顶全是正面），是皮肤折出来的棱。

```powershell
# 握法：她的游戏动作（低模独有的剑骨接到高模上）→ 量技能 1 第 18–48 帧的正手握 → 装到预制体上
python tools\roe_clips.py b14
python tools\roe_grip.py _work\roe_clips\b14\skill_01.json b14 --bones Wp_01 --frames 18-48 --slack 0.02 --turn 15
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeWeaponGrip.Build -Graphics -Extra '-roeChars','b14'
# 候选：导入、拍、视频、总览图、检查图
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/b14_sword_candidates.json','-roeTips','E:\code\othercode\roe_fighter_unity\_work\sword_tips_b14'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','b14_sword_candidates','-roeChars','b14','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_b14_cand'
python tools\strike_demo_video.py _work\strike_takes_b14_cand out\b14_sword_candidates.mp4 --pack tools/motionpacks/b14_sword_candidates.json --id b14
python tools\takes_sheet.py _work\strike_takes_b14_cand out\b14_sword_candidates_sheet.jpg --pack tools\motionpacks\b14_sword_candidates.json
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Sheet -Graphics -Extra '-roePack','b14_sword_candidates','-roeChars','b14'
python tools\strike_sheet.py out\motion_sheets\b14_sword_candidates out\b14_sword_candidates_moves_sheet.jpg --pack tools\motionpacks\b14_sword_candidates.json
# 定下的四招
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/b14_sword.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','b14_sword','-roeChars','b14','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_b14_sword'
python tools\strike_demo_video.py _work\strike_takes_b14_sword out\b14_sword_strikes.mp4 --pack tools/motionpacks/b14_sword.json --id b14
python tools\takes_sheet.py _work\strike_takes_b14_sword out\b14_sword_moves_sheet.jpg --pack tools\motionpacks\b14_sword.json --cols 12 --tile 200
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Sheet -Graphics -Extra '-roePack','b14_sword','-roeChars','b14','-roeShots','10'
python tools\strike_sheet.py out\motion_sheets\b14_sword out\b14_sword_check_sheet.jpg --pack tools\motionpacks\b14_sword.json --cols 10
# 右肩：重建裸体（-roeFaces 1 另拍胸口特写和三角面正反，放进 _work 不动 out），检查图，场景，录像
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','b14','-roeFaces','1','-roeOut','E:\code\othercode\roe_fighter_unity\_work\b14_faces'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','b14'
python tools\burst_sheet.py --chars b14
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','b14','-roeP2','a08','-roeSeconds','120','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_b14_sword'
python tools\make_video.py _work\fight_b14_sword out\fight_cpu_match_b14_sword.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','b14','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_b14'
python tools\burst_video.py _work\burst_demo_b14 out\clothes_burst_demo_b14.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
```

### Kart 鹤袍的粉白换色款（b14_outfit1）加入战斗（10-17）

用户 10-17 问"粉白换色款要不要做成新角色"，回答"要"。做成了新角色 PINK CRANE KART（选人卡片上写 Pink Crane），名单里排在 CRANE KART 后面。

**游戏里换色款是怎么做的**

- 网格、骨架、动作全用 b14 的（`chara_armor_pc_b14_*`），没有自己的模型包。
- 自己的一套：
  - 材质 `chara_mat_armor_pc_b14_outfit1_*`、贴图 `chara_tex_armor_pc_b14_outfit1_*`：白发粉梢、粉白长袍、红腰带、红宝石，剑也是自己的贴图；
  - 展示预制体 `meta_armor_pc_b14_outfit1`（渲染器名字和 b14 的一样）；
  - 玩法单位 `suit_kart_14_outfit1`：三个技能的动作和 b14 相同，特效全是粉色版，技能 1 还多了两段光效。
- 10-11 导入 b14 时，这些材质、贴图、展示预制体已经跟着进了工程（`Assets/ROE/b14` 里带 outfit1 的文件夹），`import_ripped.py` 也在清单里记下了它的展示预制体，只是没做成角色。
- 游戏的热更新包在 `%USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\AssetBundles`，游戏运行时优先用这里的：
  - 换色款的材质包今天（10-17）刚更新过，逐项比过，参数和工程里那版完全一样；
  - 它的玩法单位 10-01 更新过，比安装目录里的新，改的是超必杀的命中时间（2.55 → 2.65 秒）和几段特效的时间。
  - `tools/timeline_dump.py`（技能表读时间轴用的）现在优先读 LocalLow 里的。

**做法**

- 新工具 `tools/roe_outfit.py b14 outfit1`：在角色清单里加一个角色 `b14_outfit1`，内容抄 b14 的条目，只把展示预制体换成换色款的，并记下它是谁的换色款（`outfitOf`）。
  代号照游戏自己的命名（`pc_b14_outfit1`、`wp_b_14_outfit1`）。以后重新导入 b14，要再跑一次这个工具。
- 和所属那套共用的东西，找不到自己的就用 b14 的（`RoeManifest.SuitOf`）：
  - 爆衣规则：`Editor\Burst\b14.json` 里按材质名匹配的地方改成两套都能匹配（`^pc_b14_hd_(outfit1_)?body1$`）；
  - 握剑：`tools/roe/b14_grip.json`；
  - 建场景时按角色查的表（普通攻击 `-roeOwnStrikes`、腿 `-roeLegRoll`、站架 `-roeGuard` 等）：换色款没有自己的一项时用 b14 的，所以她也用剑招 `b14_sword`。
- 换色款的剑只有战斗低模的材质 `wp_b_14_outfit1_ld`。找武器材质的代码以前只找 `_hd`，现在没有 `_hd` 时用 `_ld`。
- 头发：换色款用的是游戏较新的各向异性头发着色器 `Pinkcore/Heros/AnisoHair`，工程里没有对应的，第一次建出来头发是一整块洋红色。
  - 现在把它对到我们的 `RoeHair`（`import_ripped.py` 的 `SHADER_MAP`）。高光偏移、收紧、两层高光颜色这些参数两边同名，能直接用。
  - 用不上的：它的打包贴图（高光 / 遮蔽 / 阴影遮罩）、各向异性强度、透光。看着接近游戏，但不是逐条照游戏重写的；要完全一样，得像别的着色器那样照游戏的反汇编重写一个。
- `tools/roe_showcase.py`：换色款没有自己的模型包，选人展示的时间轴从 b14 的包里读，台词映射用它自己的（和 b14 是同两句）。
- 布料、物理：游戏大厅的 Magica Cloth 2 设置和 b14 完全一样（`roe_mc2_dump.py` 读出来逐项相同）。
- 选人界面成了 13 张卡片，每张窄了，CRANE KART 和 PINK CRANE KART 两个长名字压在一起。
  现在卡片上的名字放不下时自动缩小字号，必要时折成两行（`FightHud`）。

**效果**

- 三个技能都打中（对 Inase）：技能 1 掉 150，技能 2 掉 190，超必杀掉 360 并打掉一段衣服。视频 `out\b14_outfit1_skills.mp4`。
- 普通攻击和 b14 一样是剑招四招，视频 `out\b14_outfit1_sword_strikes.mp4`。
- 爆衣四段，最后是全身。右肩照左肩镜像取权重这一套也跟着 b14 的规则一起生效。
  - 爆衣演示 `out\clothes_burst_demo_b14_outfit1.mp4`；
  - 检查图 `out\clothes_burst\unity\b14_outfit1_sheet.png`；
  - 握剑检查图 `out\weapon_grip\b14_outfit1_0..2.png`。
- 电脑对电脑：粉白对蓝白（对 b14，60 秒），最后 CRANE KART 用技能把 PINK CRANE KART 打到 K.O.，视频 `out\fight_cpu_match_b14_outfit1.mp4`。
- 选人界面 `out\select_screen_demo_1017.mp4`。

```powershell
# 1. 清单里加角色（b14 已经导入过；换色款的材质、贴图、展示预制体那时一起进来了）
python tools\roe_outfit.py b14 outfit1
# 2. 技能：玩法单位（用 LocalLow 里热更新过的那份）→ 还原 → 导入 → 技能表；选人展示；大厅物理
#    （AssetRipper 服务开着，见"从零重做一遍"）
copy "%USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\AssetBundles\gameplay_prefab_suit_kart_14_outfit1.ab" _work\bundles_skills_b14_outfit1\
python tools\rip.py --bundles _work\bundles_skills_b14_outfit1 --out _work\ripped_skills_b14_outfit1 --log _work\assetripper.log
python tools\import_skills.py suit_kart_14_outfit1 --src _work\ripped_skills_b14_outfit1\ExportedProject\Assets
python tools\skill_sheet.py suit_kart_14_outfit1 b14_outfit1
python tools\roe_showcase.py b14_outfit1
python tools\roe_mc2_dump.py "Assets\ROE\b14\meta_armor_pc_b14_outfit1_hd_ld\pc_b14_outfit1_hd.prefab" --fighter b14_outfit1
# 3. Unity（爆衣规则、握法、剑招都沿用 b14 的）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFighterBuilder.BuildAll -Extra '-roeChars','b14_outfit1'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHumanoidClips.ConvertAll -Extra '-roeChars','b14_outfit1'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.Run -Extra '-roeChars','b14_outfit1'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeHelperFit.FitSkirt -Extra '-roeChars','b14_outfit1'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeWeaponGrip.Build -Graphics -Extra '-roeChars','b14_outfit1'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','b14_outfit1'
python tools\burst_sheet.py --chars b14_outfit1
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
# 录像
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.SkillReel -Graphics -Extra '-roeP1','b14_outfit1','-roeP2','a08'
python tools\skill_reel_video.py out\b14_outfit1_skills.mp4 _work\skills_b14_outfit1
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','b14_sword','-roeChars','b14_outfit1','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_b14_outfit1'
python tools\strike_demo_video.py _work\strike_takes_b14_outfit1 out\b14_outfit1_sword_strikes.mp4 --pack tools/motionpacks/b14_sword.json --id b14_outfit1
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','b14_outfit1','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_b14_outfit1'
python tools\burst_video.py _work\burst_demo_b14_outfit1 out\clothes_burst_demo_b14_outfit1.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','b14_outfit1','-roeP2','b14','-roeSeconds','60','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_b14_outfit1'
python tools\make_video.py _work\fight_b14_outfit1 out\fight_cpu_match_b14_outfit1.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.SelectDemo -Graphics -Extra '-roeOut','E:\code\othercode\roe_fighter_unity\_work\select_demo_1017'
python tools\make_video.py _work\select_demo_1017 out\select_screen_demo_1017.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
python tools\make_manual.py --shot select=_work\select_demo_1017\frames\00160.jpg
```

### Inase 站游戏里的姿势（10-17）

用户 10-17："inase的默认动作能换成ROE中的inase的么"。

- 两招之间的站架换成了她在 ROE 里的战斗站姿 `idle_01`：直身站着，右手扶着巨剑，剑尖拄在脚边，左手叉腰。原来是动作包的拳击架势，剑扛在肩上。
- 做法和 Luf 一样（见"Luf 站游戏里的姿势；技能键 1、2、3"一节）：`-roeGuard` 的默认值加了 `a08=stance`，`a08_guard_stance.anim` 是 `idle_01` 的人体曲线。
  走路、侧步、四个剑招、三个技能都没变，出招从游戏站姿接过去，收招回到游戏站姿。
- 剑：
  - 0 层一直在同步放她游戏的 `idle_01`，里面就有巨剑的关键帧（两把剑重合成一把，剑尖拄地、手握剑柄）。
  - 以前动捕动作里一直套着挥砍的握法（`RoeGrips`，取自技能 1）。站姿也套的话，剑会按挥砍的角度挂在手上，指向不对。
  - 现在站在游戏站姿里时不套握法，剑就是游戏站姿里的样子；一出招、往前或往后走，握法随着站架淡出慢慢接管，剑转进挥砍的握法（`FighterRig`）。
  - 侧步时上半身还是站架的，所以剑也留在站姿里，跟着身体走。
- 对比视频 `out\a08_roe_stance.mp4`（30 秒，`RoeClothDemo.Guards`，左原来、右现在）：同一套按键——站架、前进、后退、两边侧步、四个普通攻击、三个技能。
  对比图 `out\a08_roe_stance.jpg`：上排正面、下排左侧。
- 要换回拳击架势：建场景时加 `-roeGuard g04=stance,a08=pack`（这个参数会替换整张表，Luf 的要一起写上）。

```powershell
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Guards -Graphics -Extra '-roeChars','a08','-roeScript','all','-roeView','front','-roeOut','E:\code\othercode\roe_fighter_unity\_work\a08_guard_all_front'
python tools\cloth_demo_video.py _work\a08_guard_all_front out\a08_roe_stance.mp4 --variants guard_pack,guard_stance
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Guards -Graphics -Extra '-roeChars','a08','-roeScript','stance','-roeView','left','-roeOut','E:\code\othercode\roe_fighter_unity\_work\a08_guard_stance_left'
```

### 剑星 Eve 的粉色紧身衣（Cooling Suit，`eve_nikke06`）（10-17）

用户 10-17："eve中有个粉色的衣服的角色有哪些帮我查一下，我要找一个加入到战斗角色里"，看过候选后说"Cooling Suit 加入到角色里"。

**剑星里哪些衣服是粉色的**（归档的 148 套 Eve 服装，按预览图里粉色像素的占比排，再逐张看）

| 服装 | 说明 |
|---|---|
| Cooling Suit（Alice，`CH_P_EVE_Nikke_06`） | NIKKE 联动 DLC：粉色紧身衣、粉色蓬松外套、白袜球鞋 |
| Pink Bear（`CH_P_EVE_45_TypeB`） | 一整套粉色熊玩偶服，连头套 |
| Peony（牡丹，`CH_P_EVE_33_Body`） | 珊瑚粉的旗袍式紧身衣 |

- 看着像粉、其实是红色系的：La Vie en Rose、Angelic Rose、Resonance、6th V2 (OrangeRed)。
- ripper_tpose 以前按贴图做过一次同样的调查（`docs/stellar-blade-eve-outfits.md` 第 4 节），结论也是真正的粉色只有 Pink Bear 和 Cooling Suit 两套。

**做法**：和 eve09、eve37 一样（`docs/stellar-blade-eve.md` 第 5 节），代号 `eve_nikke06`。

- 模型：ripper_tpose 归档的 `Eve_CH_P_EVE_Nikke_06.blend` → `tools/sb_fbx.py` → `Assets/SB/eve_nikke06`（不入库）。204 根骨，4 个网格。
- 联动 DLC 的东西在游戏包的 `SB/Content/DLC_2/Art/Character/PC/CH_P_EVE_Nikke_06/` 下，不在本体的 `Art/Character/PC` 下：
  - 贴图：`tools/sb_textures.py --mi-dir DLC_2/Art/Character/PC/CH_P_EVE_Nikke_06/Mat`。
  - 物理：`tools/sb_physics.py` 新加了 `--dlc DLC_2`，衣服自己的动画蓝图、物理资产、马尾碰撞改到 DLC 路径下找；主动画蓝图和头发还是本体的。
- 贴花：腿上 "SUPERB" 的字是一个贴花材质。游戏里它在材质实例上把混合方式覆盖成了"遮罩"（裁剪阈值 0.333），透明度在颜色图的 alpha 里。
  `sb_textures.py` 以前没读这种覆盖，导出成不透明，腿上会是一块黑方块。现在遇到覆盖成遮罩的，保留颜色图的 alpha，按游戏的阈值裁剪。
- 物理全用剑星自己的（游戏数据读出来）：6 个弹簧骨（胸、臀、大腿扭转骨），17 个 KawaiiPhysics 节点（头发，加上这套衣服自己的颈部褶边、胸前两条带子、背后的飘带和环、肩上的细绳、腋下的挂件），马尾 7 节刚体链。
- 发型：游戏里还有一个配套的 Alice 发型 `EVE_HR_Nikke_06`，是单独装备的。这次和 eve09、eve37 一样用 Eve 默认的黑马尾。
  同一天后来换成了 Alice 的发型，见"Eve 粉色紧身衣：Alice 的发型、她自己剑星里的招式"一节。
- 格斗定义 `tools/sb/eve_nikke06.json`：照 eve09 的。站架、走、四个普通攻击用动作包；受击、倒地、起身和三个技能用 UFE 2 的。
  同一天后来全换成了她自己剑星里的动作（同上一节）。
- 名单默认加在 eve37 后面。选人卡片上是 EVE，名字下面写 Cooling Suit (Alice, NIKKE)。

**爆衣四段**（`Editor\Burst\eve_nikke06.json`，检查图 `out\clothes_burst\unity\eve_nikke06_sheet.png`）

| 段 | 掉什么 |
|---|---|
| 1 | 外套和领子（最外面一层，掉了最显眼） |
| 2 | 胸前的带子、背后的环和飘带、腿上的字 |
| 3 | 球鞋和袜子、手套 |
| 4 | 紧身衣 |

- 衣服是一个网格、三种材质：UV1 是紧身衣、袜子、球鞋、手套；UV2 是外套、领子、带子；第三种是腿上的字。按材质和挂的骨头分组。
- 衣服把她全身都包住了，里面没有她自己的皮肤可以换。底下垫 Eve 的裸体底模（`Assets/SB/eve_nude`，和 eve09、eve37 一样），每块不透明的衣服掉了以后，它下面的身体才画出来（`reveal`）。

**录像**

- 爆衣演示（Inase 打她，两局四段全掉）：`out\clothes_burst_demo_eve_nikke06.mp4`。
- 电脑对电脑（对 eve09，60 秒）：`out\fight_cpu_match_eve_nikke06.mp4`。
- 选人界面（14 张卡片）：`out\select_screen_demo_1017.mp4`。

```powershell
# 1. 模型（Blender 3.6，读归档的 .blend，不保存）
& 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup 'E:\game_export\StellarBlade\Eve\blend\CH_P_EVE_Nikke_06\Eve_CH_P_EVE_Nikke_06.blend' --python tools\sb_fbx.py -- Assets\SB\eve_nikke06 eve_nikke06
# 2. 贴图、物理（联动 DLC 的路径）
python tools\sb_textures.py Assets\SB\eve_nikke06 --mi-dir DLC_2/Art/Character/PC/CH_P_EVE_Nikke_06/Mat
python tools\sb_physics.py Assets\SB\eve_nikke06 --outfit CH_P_EVE_Nikke_06 --dlc DLC_2
# 3. Unity：建角色、爆衣检查、场景
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','eve_nikke06'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','eve_nikke06'
python tools\burst_sheet.py --chars eve_nikke06
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
# 录像
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','eve_nikke06','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_eve_nikke06'
python tools\burst_video.py _work\burst_demo_eve_nikke06 out\clothes_burst_demo_eve_nikke06.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','eve_nikke06','-roeP2','eve09','-roeSeconds','60','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_eve_nikke06'
python tools\make_video.py _work\fight_eve_nikke06 out\fight_cpu_match_eve_nikke06.mp4
```

### Eve 粉色紧身衣：Alice 的发型、她自己剑星里的招式（10-17）

用户 10-17："用 Alice 的专属发型 ，用 eve里自己的招式"。

**Alice 的发型**（`tools/sb_hair.py`，图 `out\eve_nikke06_alice_hair.jpg`）

- 游戏里 Cooling Suit 配套的发型 `EVE_HR_Nikke_06`：NIKKE 里 Alice 的粉色双马尾、刘海、带麦克风的猫耳耳机。它是单独装备的，在 `SB/Content/DLC_2/Art/Character/PC/00_HR/` 下。
  用 CUE4Parse 命令行导成 ActorX（.psk）：63,076 个顶点，289 根骨，骨架的根是头骨 `Bip001-Head`。
- `sb_hair.py` 在 Blender 3.6 里先于 `sb_fbx.py` 跑（同一个会话，归档的 .blend 不保存，`sb_fbx.py` 照样找得到它旁边的贴图）：
  - 去掉 .blend 里 Eve 默认的发帽和两条马尾，以及只有它们用的 38 根骨：`Hair_Root` 和它下面的前额、侧面发束，马尾链 `Ab-TL-HairB*`，后颈短发 `Ab_SHair_*`。
    新发型有同名的前额、侧面发束骨，但位置不同，所以先删旧的。
  - 用 PSK 插件导入新发型，按头骨的整个静止矩阵对齐（和 `validate_eve.py` 挂后颈短发一样），合进 `Eve_Armature`，去掉它自己那根头骨（顶点组落到身体的头骨上）。
  - 核对：同名发束新旧位置只差 2–6 厘米、方向一致，没有前后反。
- 贴图（`tools/sb_textures.py`）：
  - `--mi-dir` 现在可以给多个（衣服的材质目录，发型的材质目录）。
  - 发型材质 `MI_EVE_HR_Nikke_06` 的父材质是 `MA_CH_Hair`。透明度用它自己的 `HairTexture`（`EVE_HR_Nikke_06_ADIR`：红通道是发片透明度，后面是深度、发丝编号、发根渐变）。
    实例里另外挂的 `Alpha` 是通用的马尾图集，用它后脑勺会透出一道缝（Blender 里两种都渲染对比过）。
  - 发色是发根 / 发梢颜色 × 亮度 × 2（以前给 Eve 的棕发定的）。浅粉色乘 2 会超过 1，变成近白；现在超过 1 时整体按比例压回，色相不变，棕发不受影响。结果 #FFC2E1。
  - 耳机和发绳用衣服的 UV2 材质（导入时直接用 .blend 里同名的那个）。
- 物理（`tools/sb_physics.py --hair EVE_HR_Nikke_06 --hair-dlc DLC_2`）：
  - 发型自己的动画蓝图里有 65 个 KawaiiPhysics 节点：双马尾按分叉一段一段算，加上刘海和侧发。碰撞胶囊都在头骨上（游戏里双马尾只和头碰）。
  - 这个发型没有马尾组件，所以没有刚体马尾；主蓝图里管默认马尾的节点也跟着去掉了。现在共 75 个节点（衣服的 10 个照旧）。
- 爆衣不受影响：发型和耳机不在爆衣里。

**她自己的招式：导出**（`tools/sb_anims.py`）

- 她的动画在 `SB/Content/Art/Character/PC/CH_P_EVE_01/Animation/`（4000 多个）。导了 200 多个：剑技（正式版的轻攻击、重攻击，Beta 技能、Burst 技能、反击，原型招式）、受击、倒地、躺地、起身、战斗待机、锁定走路。
- `Proto_*` 是每秒 30 帧，`P_Eve_*` 是 60 帧。CUE4Parse 写的帧率是"帧数 / 时长"（91 帧 3 秒写成 30.33），实际间隔是时长 / (帧数 − 1)。
- 位移在 Root 上（`_Root` 版和 `P_Eve_` 版前冲 1–6 米），这些片段里 Root 不转。
- 角色界面、Social 目录的动画用 ACL 压缩，这个命令行版本缺 ACL 的原生库，导不出来（没用到）。

**转成人形动画**（`SbAnims`，照 Fiona 的 `VdfAnims`）

- 一帧一帧把游戏的骨骼旋转套到她的骨架上（Biped 和道具骨；Bip001 和道具骨连位置一起；Root 带着整副骨架走），再用她自己的 avatar 读出人形姿势。
- 关键帧怎么读：Fiona 的办法（片段里不动的骨等于参考姿势）在这里不成立：她握剑的手指整段都不动。改成两步：
  - 骨骼到父骨的偏移不会变，比一比就知道要不要按 Y 镜像（镜像 0.0 毫米，不镜像 116 毫米）；
  - 四元数取不取共轭，看哪种读法让她站起来（头比脚高 112 厘米，另一种 −80 厘米）。
  - 结果是"Y 镜像 + 共轭"，和 Python 里按骨架层级算正向运动学的结论一样。
- 组件空间：剑星的角色朝 +X，`sb_fbx.py` 把她转成朝 −Y，所以 UE 的 (x, y, z) 是她模型的 (y, z, x)。
- 回放检查：转出来的动画放回她身上，手、脚、头、剑尖和游戏姿势相差 0.0 厘米（同一副骨架、同一个 avatar，来回没有损失）。

**她的剑**（血刃，`tools/sb_weapons.py`）

- 游戏的 `CH_W_Sword_01`，展开状态，全长 1.4 米，三个材质槽。刃口那个槽在游戏里透明度为 0（技能时才亮），去掉了。贴图是父材质 `CH_W_Sword_02` 自己的：颜色、法线、ORM。
- 挂在哪：
  - 每个剑技动画都给 `Bip001-Prop1` 打关键帧。它在 Bip001 下面，不在手下。普通招式里它跟着手走（相对右手转 86°、偏 6 厘米）；Burst 回旋斩、Beta 锁链突刺里剑离手飞出去（最远 1.2 米）。
  - 用骨架正向运动学核对过：游戏是让剑自己的根骨 `Weapon_Root` 和 Prop1 重合。这样锁定走路时剑尖朝正前方、离地 74 厘米；如果直接把剑的组件放在 Prop1 上，剑会朝下插进地里。
- 人形动画里没有 Prop1，所以剑挂在她右手下面的持剑点 `SB_Sword` 上（绑定姿势时在 Prop1 的位置）。她自己的每个动画都给持剑点打关键帧：游戏里 Prop1 相对右手在哪，持剑点就在哪。
  普通招式里剑在手里，技能里剑照游戏飞出去。
- `VdfFighter.AttachWeapons` 扩展了：武器目录可以在 `Assets/SB/<id>` 下；`holder` / `prop`（持剑点）、`rootTurn`（网格先转到它根骨的坐标系）、`drop`（去掉的材质槽）。
  `SbFighter.Build` 最后挂剑。打中用剑尖（`RoeBlade`），和 Fiona 一样。

**挑招**：19 个候选（`tools/motionpacks/sb_eve_candidates.json`）

- 视频 `out\eve_nikke06_sword_candidates.mp4`：每招先实速、再半速。总览图 `out\eve_nikke06_sword_candidates_sheet.jpg`：按键后每 0.1 秒一格，红条是能打中的时间。
- 先把每个剑技整段导进来，量剑尖每帧的位置（`RoeMotionPacks.Import -roeTips`），按剑尖的挥动段落定每招截到哪里。

**默认四招**（`tools/motionpacks/sb_eve.json`）

| 键 | 招式 | 出处 | 能打中 | 够到 | 段 |
|---|---|---|---|---|---|
| A | 冲步横斩 | 正式版轻攻击 1（`P_Eve_Sword_Normal_LightAttack1_Connect1_Rfront`）0–0.75 秒 | 0.10–0.25 秒 | 2.51 米 | 中段 |
| B | 回身低斩 | 轻攻击 2（`LIghtAttack2_2`）0–1.1 秒：转身、深弓步往下斩 | 0.45–0.63 秒 | 3.20 米 | 下段 |
| C | 两段斩 | 重攻击 4（`StrongAttack4`）0–1.2 秒 | 0.07–0.77 秒 | 4.09 米 | 中段 |
| D | 挑空斩（击倒） | 地面挑空（`Sword_Ground_Upper`）0–1.0 秒：突进、往上挑 | 0.32–0.47 秒 | 3.00 米 | 中段 |

- 够到的距离包括游戏里的前冲（1–2 米）：动画里的前冲扣掉，对战时按时间加回去（`Move.travel`），总的走位和游戏一样。
- 站架：锁定敌人时的战斗待机（剑在身后压低）。走、退、跑：锁定时的走路和跑。
- 受击：站立轻受击（`Result_Hit_Stand_Light_Fw`）。倒地：被打飞仰倒（`Result_Knock_Down_Backward`）。躺地：仰躺循环（`Result_State_KnockDown_L`）。起身：`Getup_Center`。
- 开场：举剑到脸前（`P_Eve_Sword_Normal_Buff1`）。胜利：跃起转剑、落成 Burst 架势（`P_Eve_Sword_Burst_MeleeCombatBuff1`）。
- 技能：
  - 技能 1：Beta 锁链突刺（`P_Eve_Sword_Beta_ChainStab1_Ex`），剑脱手往前射出去，打三下（0.80 / 1.00 / 1.42 秒）。
  - 技能 2：Beta 蓄力斩（`P_Eve_Sword_Beta_ChargeSlash2_Ex`），腾空两段斩（0.58 / 0.92 秒）。
  - 超必杀：Burst 回旋斩（`P_Eve_Sword_Burst_SpinAttack1`），空中倒转回旋（0.55 / 1.30 / 1.88 秒）。
  - 技能是"先滑到对手面前再出招"：`radius` 是停在多远，锁链突刺够得远，给 1.6 米，另外两个 1.0 米。
- 没选的：重攻击 5（扑倒在地上滑）、重攻击 7 / 8（扑斩、翻滚收尾）、新月斩（起手太慢）、Beta 破盾（翻滚冲 6 米）、原型招式（原地小动作）。
- 脚：以前是 `"feet": "bind"`（套别的动作时，脚踝固定在绑定角度配她的鞋）。现在是她自己的动作，脚踝跟着游戏走，去掉了。
- eve09、eve37 还是原来的动作包和 UFE 2 的技能（这次只改了 eve_nikke06）。同一副骨架，要用就把 `tools/sb/eve09.json` 照这个改。
  同一天后来也换了，见下一节"eve09、eve37 也用她自己剑星里的招式"。

**录像**

- 四招：`out\eve_nikke06_sword_strikes.mp4`（每招先实速再半速）。总览图 `out\eve_nikke06_sword_moves_sheet.jpg`。
- 电脑对电脑（对 eve09，90 秒）：`out\fight_cpu_match_eve_nikke06_own.mp4`。第一回合她把 eve09 打到 279 血（两次打倒），62 秒时被 eve09 的技能打倒；第二回合录到 90 秒时她 660 血、eve09 581 血。
- 爆衣演示重录（Inase 打她）：`out\clothes_burst_demo_eve_nikke06.mp4`。

```powershell
# 1. 发型：导出 Alice 的发型（.psk），装到归档的 .blend 上，再导 FBX（.blend 不保存）
& 'E:\tools\cue4parse_cli_ff7\cue4parse.exe' -i 'D:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB\Content\Paks' -g GAME_StellarBlade -m 'E:\game_export\StellarBlade\_meta\mappings\StellarBlade_1.1.0.usmap' -o _work\sb\alice_hair -y -p 'SB/Content/DLC_2/Art/Character/PC/00_HR/EVE_HR_Nikke_06/EVE_HR_Nikke_06.*'
& 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup 'E:\game_export\StellarBlade\Eve\blend\CH_P_EVE_Nikke_06\Eve_CH_P_EVE_Nikke_06.blend' --python tools\sb_hair.py --python tools\sb_fbx.py -- Assets\SB\eve_nikke06 eve_nikke06 --hair _work\sb\alice_hair\SB\Content\DLC_2\Art\Character\PC\00_HR\EVE_HR_Nikke_06\EVE_HR_Nikke_06.psk
python tools\sb_textures.py Assets\SB\eve_nikke06 --mi-dir DLC_2/Art/Character/PC/CH_P_EVE_Nikke_06/Mat --mi-dir DLC_2/Art/Character/PC/00_HR/EVE_HR_Nikke_06/Materials
python tools\sb_physics.py Assets\SB\eve_nikke06 --outfit CH_P_EVE_Nikke_06 --dlc DLC_2 --hair EVE_HR_Nikke_06 --hair-dlc DLC_2
# 2. 剑和动作：剑的贴图和清单，导出她的动作，建角色（挂剑），转人形动画
python tools\sb_weapons.py Assets\SB\eve_nikke06
python tools\sb_anims.py export
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','eve_nikke06'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbAnims.Import -Graphics -Extra '-roeSb','eve_nikke06'
# 3. 候选、定下的四招、场景
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/sb_eve_candidates.json','-roeTips','E:\code\othercode\roe_fighter_unity\_work\sword_tips_eve_cand'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/sb_eve.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','eve_nikke06'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
# 4. 录像
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','sb_eve_candidates','-roeChars','eve_nikke06','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_eve_cand'
python tools\strike_demo_video.py _work\strike_takes_eve_cand out\eve_nikke06_sword_candidates.mp4 --pack tools/motionpacks/sb_eve_candidates.json --id eve_nikke06
python tools\takes_sheet.py _work\strike_takes_eve_cand out\eve_nikke06_sword_candidates_sheet.jpg --pack tools\motionpacks\sb_eve_candidates.json
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','sb_eve','-roeChars','eve_nikke06','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_eve_final'
python tools\strike_demo_video.py _work\strike_takes_eve_final out\eve_nikke06_sword_strikes.mp4 --pack tools/motionpacks/sb_eve.json --id eve_nikke06
python tools\takes_sheet.py _work\strike_takes_eve_final out\eve_nikke06_sword_moves_sheet.jpg --pack tools\motionpacks\sb_eve.json --cols 12 --tile 200
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','eve_nikke06','-roeP2','eve09','-roeSeconds','90','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_eve_nikke06_own'
python tools\make_video.py _work\fight_eve_nikke06_own out\fight_cpu_match_eve_nikke06_own.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','eve_nikke06','-roeFoes','a08','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_eve_nikke06_own'
python tools\burst_video.py _work\burst_demo_eve_nikke06_own out\clothes_burst_demo_eve_nikke06.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
```

### eve09、eve37 也用她自己剑星里的招式（10-17）

用户 10-17 看完粉色紧身衣 Eve 的招式后说"换"（接的是"另外两套 Eve 还在用原来的动作包，要的话可以照这套换成她自己的招式"）。

- `tools/sb_own_moves.py eve09 eve37`：把粉色紧身衣那套照搬过去。
  - 写 `tools/motionpacks/sb_eve09.json`、`sb_eve37.json`：同样的四招、站姿、走、退、跑。
  - 改 `tools/sb/eve09.json`、`eve37.json`：动作包、站姿（她自己的）、脚（不再固定在绑定角度）、受击倒地起身开场胜利和三个技能。名字、服装、物理照旧。
- 每套用自己的动画副本：剑的持剑点在动画里是按"从角色根到持剑点"的路径打关键帧的，路径开头是她骨架的名字（就是代号），三套不能共用一份。
  只导用得上的 17 个（`SbAnims.Import -roeOnly`，命令见下），不是全部 200 多个（全部要 1.4 GB）。
- 剑：`tools/sb_weapons.py Assets/SB/eve09`（eve37 一样），`SbFighter.Build` 挂上。
- 百褶裙（eve37）裙子的 Control Rig 和 KawaiiPhysics 照旧，跟着她自己的动作走。
- 现在三套 Eve 的招式一样，外观、物理、爆衣各是各的。
- 电脑对电脑（eve09 对 eve37，90 秒）：`out\fight_cpu_match_eve09_eve37_own.mp4`。第一回合打满时间，最后一刻 eve37 的技能把 eve09 打到 0 血，eve37 胜（剩 579 血）；两人都被打倒过、都放了技能。

```powershell
python tools\sb_own_moves.py eve09 eve37
python tools\sb_weapons.py Assets\SB\eve09
python tools\sb_weapons.py Assets\SB\eve37
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','eve09,eve37'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbAnims.Import -Graphics -Extra '-roeSb','eve09,eve37','-roeOnly','<sb_own_moves.py 打印的那串>'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/sb_eve09.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/sb_eve37.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','eve09','-roeP2','eve37','-roeSeconds','90','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_eve09_eve37_own'
python tools\make_video.py _work\fight_eve09_eve37_own out\fight_cpu_match_eve09_eve37_own.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
```

### 剑星的渡鸦（Raven，`raven`）加入战斗（10-18）

用户 10-18："把渡鸦加到角色里，动作用游戏内的，都要检查爆衣效果"。

渡鸦是剑星里的 Boss（`SB/Content/Art/Character/Monster/CH_M_NA_53`）：白色紧身战衣、黑色长发、一把 1.55 米的长剑。
模型、贴图、物理、武器、动作全部照游戏读；爆衣用的是游戏自带的、她战衣下面完整的裸体网格 `CH_M_NA_53_1`。

**模型**（`tools/sb_parts.py` + `tools/sb_fbx.py`）

- 来源：ripper_tpose 的 `scripts/stellarblade/npc`（10-18 导出的 17 个剑星女角色）存到 E 盘的 `Raven\blend\Raven\Raven.blend`：身体、脸、头发三个网格，各带一副骨架（脸和头发的骨架挂在身体骨架下面）。
- 新的 `sb_parts.py` 在 `sb_fbx.py` 之前跑（同一个 Blender 会话，.blend 不保存）：脸和头发的骨架并进身体的骨架。同名骨直接用身体的（静止位置相差不到 0.001 厘米），身体没有的 25 根头发骨按原样加进来，三个网格都改由身体骨架驱动。
- 结果 `Assets/SB/raven`：388 根骨、3 个网格（`CH_M_NA_53.001` 身体、`CH_M_NA_53_Facial.001` 脸、`CH_M_NA_53_Hair.001` 头发）、14 个材质，眼睛烘焙成一张图。
- `sb_fbx.py` 认得其他角色的材质名了：泪线 / `_fluid` 隐藏，`Lashes` 算睫毛，`_Eyes` / `Eyeball` 算眼睛，`BaseBody` 算皮肤。

**贴图**（`tools/sb_textures.py`）

- 材质实例在三个目录：`Monster/CH_M_NA_53/Materials`（脸、头发、武器）、`PC/CH_P_EVE_11/Materials`（战衣，Eve 的 11 号服装就是渡鸦这套）、`PC/CH_P_EVE_12/Materials`（身体皮肤）。
- 新增和修改：
  - `--hair-boost 3`：她的发片落在发片贴图稀疏的细发丝区，Unity 按 0.35 裁剪会稀掉（游戏是多层抖动半透明），覆盖率先乘 3；
  - .blend 里透明度为 0 的材质一律隐藏（泪线、找不到遮罩的睫毛），不只头上的 `NewMaterial`；
  - 法线参数还认 `BakeNormal`、`BaseNormal`（她战衣的父材质 `MA_CH_M_NA_53_Costume` 叫 `BakeNormal`）；
  - 眉毛的遮罩写在父材质里（不是实例参数），用 .blend 里接好的遮罩裁剪；
  - 自发光颜色的负分量按 0 算：长发飘带 `MI_CH_M_NA_53_Hair02` 在游戏里填的是 (−43.4, 3.8, 6.9)，就是青色；
  - 布料的自发光按实例的 `Emissive_CH` 只取那一个通道（实例没设就和以前一样）。

**物理**（`tools/sb_physics.py --abp ... --physics ...`）

- 她只有一个动画蓝图 `CH_M_NA_53_AnimBP` 和一个物理资产 `CH_M_NA_53_Physics`，新加的入口直接读这两个：
  - 6 根弹簧骨（胸、臀、大腿，数值和 Eve 一样）；
  - 5 个 KawaiiPhysics 节点：背后的尾摆 `Ab-Bk-SuiteA`、前后飘带 `Ab_RV_ACC_*`；
  - 长发是刚体：物理资产里 130 个非运动学的刚体挂在 134 根发束骨 `HBN_*` 上，130 个关节，身体的 43 个运动学刚体当碰撞体。
- `RoeSbRig.KindOf` 认 `hair` 这条链（和 Eve 的刚体马尾一样算头发）。
- 建角色时核对：130 个关节的静止位置和她的骨骼差 0.0 厘米、0 度。
- 控制 rig（`CH_M_NA_53_CtrlRig`、`_Groggy`）没搬。

**她的剑**（`tools/sb_weapons.py`）

- `CH_M_NA_53_Weapon`：一把长剑，网格沿根骨的 z 方向伸展（刀尖在 z = 155 厘米），一个材质，贴图是颜色、法线、ORM。
- 挂在哪：蓝图里没有武器组件。骨架资产 `CH_M_NA_53_Skeleton` 里有个插槽 `SC_Weapon`，挂在 `Bip001-Prop1` 上，旋转 Pitch −90、Yaw 180、Roll −180。
  按 UE 的旋转矩阵算，剑的 z 正好对到 Prop1 的 x，和 Eve 的剑一样，刃沿 Prop1 的 x。
  用骨架的正向运动学核对（`tools/sb_swings.py` 同样的算法）：战斗待机时剑尖离地 54 厘米、斜向外下方，和游戏图鉴里她的立绘一致。
  如果剑的组件不转、直接放在 Prop1 上，剑尖会插进地面下 30 厘米。
- `weapons.json` 新增 `socketRotator`（UE 的 pitch、yaw、roll），`VdfFighter.ReadPsk` 用它把网格转进插槽的方向；`sb_weapons.py` 新增 `--weapon`、`--maps`、`--keep-all`、`--socket-rotator`。

**她的动作**（`tools/sb_anims.py --folder`、`SbAnims -roeSrc -roeCalib`）

- 她自己的动画在 `Monster/CH_M_NA_53/Animation/`，导了 83 个：
  - 待机、四个方向的警戒步（原地循环）、跑；
  - 剑招连段 25 个左右（Slash、SlashChain、SlashCombo、MoveCombo、ChaseCombo、BurstSpinCombo、SwordAuraCombo……），连段里 Root 前冲最远 27 米；
  - 受击 7 种、倒地（开始 / 躺 / 起身）、晕眩、浮空。
- `sb_anims.py --folder` 导别的目录；`SbAnims.Import` 新增 `-roeSrc`（只读这个目录里的 .psa：她的 `Result_*` 和 Eve 的同名）和 `-roeCalib`（她站着的片段：`M_Raven_BattleIdle01`）。
  Eve 默认读 `SB/Content/Art/Character/PC`，和以前一样。循环片段新增警戒步和晕眩循环。
- 读法和 Eve 一样（Y 镜像 + 共轭：镜像后骨骼偏移 0.0 毫米，头比脚高 130 厘米）；83 个片段放回她身上，手、脚、头、剑尖相差 0.0 厘米。

**挑招**：18 个候选（`tools/motionpacks/sb_raven_candidates.json`）

- 每个连段先按剑尖速度分出每一刀（`tools/sb_swings.py`：按游戏关键帧做骨架的正向运动学，算剑尖每帧的速度），每刀截一段当候选。
- 总览图 `out\raven_sword_candidates_sheet.jpg`。她很多连段带长距离冲刺（劈斩 4 米、连斩 5–6 米、突进斩 7 米），一招冲出好几米，不适合当普通攻击；四招从够到 1.4–2 米的里面挑。

**默认四招**（`tools/motionpacks/sb_raven.json`）

| 键 | 招式 | 出处 | 能打中 | 够到 | 段 |
|---|---|---|---|---|---|
| A | 快斩 | 斩击连段的第一刀（`M_Raven_SlashCombo`）0–0.7 秒 | 0.02–0.48 秒 | 1.64 米 | 上段 |
| B | 低斩 | Beta 蓄力连段的低斩（`M_Raven_BetaChargeCombo`）2.85–3.4 秒 | 0.18–0.33 秒 | 1.65 米 | 下段 |
| C | 前斩 | Beta 蓄力连段的前斩（`M_Raven_BetaChargeCombo`）1.2–1.9 秒 | 0.32–0.42 秒 | 1.96 米 | 中段 |
| D | 上挑（击倒） | 移动连斩的上挑（`M_Raven_MoveChainCombo`）1.2–1.95 秒 | 0.23–0.60 秒 | 1.61 米 | 中段 |

- 站架：战斗待机（`M_Raven_BattleIdle01`）。走、退：警戒步（`M_Raven_Caution_Fw` / `_Bw`）。跑：`M_Raven_Run_L`。
- 受击：`Result_Hit_Stand_Light_Fw_Dw`。倒地：`Result_State_KnockDown_S_Bw`。躺地：`Result_State_KnockDown_L`。起身：`Result_State_KnockDown_E`。
- 开场：警戒待机（`M_Raven_Caution_Cw`）。胜利：Burst 范围斩的收招（`M_Raven_BurstAreaSlashEnd`）。
- 技能（命中时间按剑尖挥动的时刻）：
  - 技能 1：剑气连段（`M_Raven_SwordAuraCombo`），0.57 / 2.10 / 3.65 秒，停在 1.6 米；
  - 技能 2：蓄力冲斩（`M_Raven_ChaseChargeSlash`），1.18 / 2.00 秒，1.4 米；
  - 超必杀：Burst 回旋连段（`M_Raven_BurstSpinCombo`），1.55 / 2.50 / 2.87 / 3.87 秒，1.2 米。

**爆衣**（`Editor/Burst/raven.json`）

- 裸体底身：游戏自带的 `CH_M_NA_53_1`，就是她战衣下面的完整身体，和战衣同一副骨架（只多一根骨），`SbFighter.BuildBase -roeSb raven_nude`。
- 战衣是一个网格四个材质：`Suit01` 白色紧身衣（含领子、前飘带的白色片）、`Suit02` 黑色漆面（肩、袖、背后尾摆、鞋、腰胯上的大量小机械件）、
  `RavensuitVinyl` 胸前到胯下的黑色漆皮、`BaseBody` 胸口露出来的皮肤（由裸体底身替换）。
- 四段：
  1. 胸前漆皮、前后飘带、背后尾摆；
  2. 黑色机械件和肩部的黑色部件（694 块）；
  3. 鞋、黑色手套；
  4. 白色紧身衣。
- 检查（`RoeBurstBuilder.Check`）：裸体底身 15538 个顶点，996 个接战衣的皮肤（间隙中位数 5.8 毫米），没有三角面被漏掉。检查图 `out\clothes_burst\unity\raven_sheet.png`。

**录像**

- 四招：`out\raven_sword_strikes.mp4`（每招先实速再半速），总览图 `out\raven_sword_moves_sheet.jpg`；候选总览图 `out\raven_sword_candidates_sheet.jpg`。
- 电脑对电脑（对 EVE 粉色紧身衣，120 秒）：`out\fight_cpu_match_raven.mp4`。第一回合时间到，EVE 555 血对渡鸦 30 血赢；第二回合录到 120 秒时渡鸦 555 血、EVE 340 血。
  她出招、放技能、被打倒、起身都照游戏的动作。
- 爆衣演示（eve09 打她，四段掉完）：`out\clothes_burst_demo_raven.mp4`；检查图 `out\clothes_burst\unity\raven_sheet.png`。
- 战衣的光泽：一开始战衣是哑光的米白色，在沙色场地里看着像皮肤；按游戏的清漆加了光泽以后重录了上面三段。
- 同一天按"都要检查爆衣效果"把全部角色的爆衣检查图重新跑了一遍（`RoeBurstBuilder.Check`，见下面的命令），其他角色的结果和以前一样。

```powershell
# 1. 模型：三副骨架并成一副，导 FBX（.blend 不保存）；裸体底身只要身体
& 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup 'E:\game_export\StellarBlade\Raven\blend\Raven\Raven.blend' --python tools\sb_parts.py --python tools\sb_fbx.py -- Assets\SB\raven raven
$env:SB_FBX_SKIP = 'Facial|Hair'; & 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup 'E:\game_export\StellarBlade\Raven\blend\Raven_Nude\Raven_Nude.blend' --python tools\sb_parts.py --python tools\sb_fbx.py -- Assets\SB\raven_nude raven_nude; Remove-Item Env:SB_FBX_SKIP
# 2. 贴图、物理、剑
python tools\sb_textures.py Assets\SB\raven --hair-boost 3 --mi-dir Art/Character/Monster/CH_M_NA_53/Materials --mi-dir Art/Character/PC/CH_P_EVE_11/Materials --mi-dir Art/Character/PC/CH_P_EVE_12/Materials
python tools\sb_textures.py Assets\SB\raven_nude --mi-dir Art/Character/PC/CH_P_EVE_12/Materials
python tools\sb_physics.py Assets\SB\raven --abp SB/Content/Art/Character/Monster/CH_M_NA_53/Blueprints/CH_M_NA_53_AnimBP --physics SB/Content/Art/Character/Monster/CH_M_NA_53/CH_M_NA_53_Physics
python tools\sb_weapons.py Assets\SB\raven --weapon SB/Content/Art/Character/Monster/CH_M_NA_53/CH_M_NA_53_Weapon --maps SB/Content/Art/Character/Monster/CH_M_NA_53/Textures/CH_M_NA_53_Weapon_A SB/Content/Art/Character/Monster/CH_M_NA_53/Textures/CH_M_NA_53_Weapon_N SB/Content/Art/Character/Monster/CH_M_NA_53/Textures/CH_M_NA_53_Weapon_ORM --keep-all --socket-rotator=-90,180,-180
# 3. 她的动作（骨架 .pskx 给 tools/sb_swings.py 用）
& 'E:\tools\cue4parse_cli_ff7\cue4parse.exe' -i 'D:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB\Content\Paks' -g GAME_StellarBlade -m 'E:\game_export\StellarBlade\_meta\mappings\StellarBlade_1.1.0.usmap' -o _work\sbnim -y -p 'SB/Content/Art/Character/Monster/CH_M_NA_53/CH_M_NA_53_Skeleton.*'
python tools\sb_anims.py export --folder SB/Content/Art/Character/Monster/CH_M_NA_53/Animation --only .
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','raven'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.BuildBase -Graphics -Extra '-roeSb','raven_nude'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbAnims.Import -Graphics -Extra '-roeSb','raven','-roeSrc','SB/Content/Art/Character/Monster/CH_M_NA_53/Animation','-roeCalib','M_Raven_BattleIdle01'
# 4. 候选、四招、爆衣检查、场景
python tools\sb_swings.py --skeleton _work/sb/anim/SB/Content/Art/Character/Monster/CH_M_NA_53/CH_M_NA_53_Skeleton.pskx --folder SB/Content/Art/Character/Monster/CH_M_NA_53/Animation M_Raven_Slash M_Raven_SlashCombo M_Raven_BetaChargeCombo   # 每一刀的时间（剑尖速度）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/sb_raven_candidates.json','-roeTips','E:\code\othercode\roe_fighter_unity\_work\sword_tips_raven'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeMotionPacks.Import -Extra '-roeSpec','tools/motionpacks/sb_raven.json'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics -Extra '-roeChars','raven'
python tools\burst_sheet.py --chars raven
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
# 5. 录像
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','sb_raven_candidates','-roeChars','raven','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_raven'
python tools\takes_sheet.py _work\strike_takes_raven out\raven_sword_candidates_sheet.jpg --pack tools\motionpacks\sb_raven_candidates.json --cols 14 --tile 150
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.StrikeTakes -Graphics -Extra '-roePack','sb_raven','-roeChars','raven','-roeOut','E:\code\othercode\roe_fighter_unity\_work\strike_takes_raven_final'
python tools\strike_demo_video.py _work\strike_takes_raven_final out\raven_sword_strikes.mp4 --pack tools/motionpacks/sb_raven.json --id raven
python tools\takes_sheet.py _work\strike_takes_raven_final out\raven_sword_moves_sheet.jpg --pack tools\motionpacks\sb_raven.json --cols 12 --tile 200
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','raven','-roeP2','eve_nikke06','-roeSeconds','120','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_raven'
python tools\make_video.py _work\fight_raven out\fight_cpu_match_raven.mp4
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstDemo.Run -Graphics -Extra '-roeChars','raven','-roeFoes','eve09','-roeOut','E:\code\othercode\roe_fighter_unity\_work\burst_demo_raven'
python tools\burst_video.py _work\burst_demo_raven out\clothes_burst_demo_raven.mp4
# 6. 全部角色的爆衣检查、exe
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeBurstBuilder.Check -Graphics
python tools\burst_sheet.py
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.BuildPlayer -Graphics
```
