# 剑星（Stellar Blade）的 Eve 加入格斗（10-05）

用户 10-05："另外把剑星里的 eve 的也加个角色进来，物理能用剑星自己的就用自己的，次选项才是用 magic cloth2"。

结论先说：

- Eve（7 代潜降服，`CH_P_EVE_09`，角色 `eve09`）进了对战，选人界面有她。
- 她身上的物理**全部照剑星游戏自己的**做，参数从游戏包里直接读出来，没有用 Magica Cloth 2：
  - 胸、臀、大腿、护腕：UE4 自带的 **SpringBone** 弹簧骨；
  - 前发、鬓发、马尾根部、领带：**KawaiiPhysics** 插件（和 Fiona 那套同一个插件，算法已经移植过）；
  - 马尾后段、背后的半透明披风：**PhysX 刚体链**（UE 的物理资产），Unity 也是 PhysX，直接照搬质量、阻尼、关节角度限制和碰撞体。
- F4 多了一个方案 `stellar`（剑星自己的物理）；`auto` 时 Eve 自动用它。
- 动作暂时用现有的动作包和 UFE 2 的受击、技能（和 Fiona 第一版一样）；她游戏里自己的动作（约 4100 段，含剑技连招）是下一步。
- 后加的百褶裙 Office Style（`eve37`，第 6 节）：裙子除了 KawaiiPhysics 还有游戏自己的 **Control Rig**，直接跑它的字节码（`RoeRigVM`）。

## 1. 模型从哪来

素材是 ripper_tpose 早就导好、归档在 `E:\game_export\StellarBlade\Eve\blend\CH_P_EVE_09\` 的 `.blend`
（`scripts/stellarblade` 的 `export_outfit.ps1` / `validate_eve.py`：服装身体 + Face_003 头 + 发型 + 马尾合在一个骨架 `Eve_Armature` 上）。

| 步骤 | 工具 | 做什么 |
|---|---|---|
| 1 | `tools/sb_fbx.py`（Blender 3.6） | 导出 FBX：Biped 骨骼名的连字符换成空格（`Bip001-L-Clavicle` → `Bip001 L Clavicle`，人形骨架按 ROE 的名字认）；她在 `.blend` 里朝 +X，转到 −Y（Unity 的前方）；厘米 → 米；两条马尾只留长的（游戏里同时只显示一条）；眼睛的虹膜在节点里是两张图混合，烘成一张；另写 `bones.json`（骨骼名、父骨、位置、蒙皮顶点数） |
| 2 | `tools/sb_textures.py` | 归档的 `.blend` 只接了颜色图。这里按**游戏自己的材质实例**（CUE4Parse 命令行读出来）取每个材质真正用的图：颜色、法线（UE 是 DirectX，绿通道翻转）、ORM（R 环境光遮蔽、G 粗糙度、B 金属度 → URP 的遮罩图）、发光（服装上的绿色光线）；头发颜色用材质里的发根 / 发梢颜色；眼睛外层的泪膜、阴影壳隐藏 |
| 3 | `tools/sb_physics.py` | 读游戏的物理设置（下一节），写 `kawaii.json` 和 `sbphysics.json` |
| 4 | Unity：`SbFighter.Build` | 材质、模型、人形骨架（和 Fiona 共用 `VdfFighter.BuildModel`，加了发光贴图）；把物理设置挂到预制体上；检查游戏的碰撞体和关节落在她身上的位置 |

坑：

- **Blender 里转骨架不刷新姿态**：`Armature.transform()` 只改了静止姿态，不打更新标记，导出器写的骨骼节点还是原来的姿态。
  第一次导出，转 0°、−90°、180° 三个 FBX 在 Unity 里一模一样，她朝 −X。现在转完对骨架和网格 `update_tag()` 再更新。
- 归档 `.blend` 里头发的颜色是近乎黑蓝的常量；游戏材质 `MA_CH_Hair` 是发根 / 发梢两个颜色乘亮度，改用游戏的。

## 2. 游戏里 Eve 的物理是什么

### 2.1 怎么读出来的

剑星是 UE 4.26 的 IoStore 包，属性是无版本序列化，要配社区的映射文件：

- 映射：`E:\game_export\StellarBlade\_meta\mappings\StellarBlade_1.1.0.usmap`；
- 工具：`E:\tools\cue4parse_cli_ff7\cue4parse.exe`（FF7 那次打过补丁的 CUE4Parse 命令行），`-g GAME_StellarBlade -f json`；
- 读出来的 JSON 缓存在 `_work\sb\research\json\`（不入库）。

和 Fiona（Vindictus，没有映射文件，手工解析字节）不同，这里每个动画节点、物理资产的所有字段都直接是 JSON。

```powershell
$G = 'D:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB\Content\Paks'
$M = 'E:\game_export\StellarBlade\_meta\mappings\StellarBlade_1.1.0.usmap'
E:\tools\cue4parse_cli_ff7\cue4parse.exe -i $G -g GAME_StellarBlade -m $M -f json -o _work\sb\research\json -y `
    -p 'SB/Content/Art/Character/PC/CH_P_EVE_01/Blueprints/CH_P_EVE_01_AnimBP_New*'
```

注意 `-p` 要带通配符（`名字*` 或 `名字.*`），写死包名会报"No matches"；多个 `-p` 一起导时偶尔有并行写文件的异常，单个导就好。

### 2.2 都在哪

| 资产 | 是什么 | 用在她身上的 |
|---|---|---|
| `CH_P_EVE_01/Blueprints/CH_P_EVE_01_AnimBP_New` | 主动画蓝图 | 27 个 SpringBone 节点（09 这套衣服有其中 8 根骨）、1 个 KawaiiPhysics 节点（马尾根）、2 个 Constraint 节点（胸骨跟随假骨 `Dm-*-Breast-Point`）、11 个 ControlRig（脚 IK、看向、游泳、自拍、受击停顿等玩法用的） |
| `00_HR/EVE_HR_01/EVE_HR_01_AnimBP` | 头发网格的后处理蓝图 | 6 个 KawaiiPhysics 节点：前发 4 条（`Ab-HairVF*`）、鬓发 2 条（`Ab_Hair_ex*`），碰撞胶囊在 `KawaiiLimitsData_Head` |
| `CH_P_EVE_09/Blueprints/CH_P_EVE_09_AnimBP` | 这套衣服的后处理蓝图 | 2 个 KawaiiPhysics 节点：领带（`Ab_NeckAcc01..06`）；1 个 ControlRig `BtoB_CtrlRig`（见 2.6） |
| `00_HR/EVE_HR_01/EVE_HR_01_Tail_PhysicsAsset` | 马尾网格的物理资产 | 马尾 9 节刚体 + 关节 |
| `CH_P_EVE_09/CH_P_EVE_09_PonytailPhysicsAsset` | 这套衣服给马尾的碰撞体 | 头、脖子、脊柱、骨盆、大小腿、上臂、前臂的形状 |
| `CH_P_EVE_09/CH_P_EVE_09_Physics` | 这套衣服身体的物理资产 | 披风左右各 7 节刚体 + 关节，身体各骨的运动学碰撞体 |
| 角色蓝图 `CH_P_EVE_01_Blueprint` | 组件设置 | 马尾是单独的 `SBPonytail` 组件，`bLocalSpaceSimulation: true`；`AdditiveMasterBoneArray` 列出马尾碰撞要跟随的身体骨骼（正好是上面那套碰撞体的骨骼）；身体组件也是 `bLocalSpaceSimulation: true` |

### 2.3 SpringBone（UE4 自带的弹簧骨）

09 这套衣服有骨的 8 个（其余 19 个是别的衣服的骨：腹部、上臂、小腿、兜帽等）：

| 骨骼 | 最大位移（cm） | 刚度 | 阻尼 | 平移 | 旋转 |
|---|---:|---:|---:|---|---|
| `Ab-L/R-Breast`（胸） | 1 | 400 | 20 | xyz | — |
| `Ab-L/R-Hip-Reg`（臀） | 2 | 400 | 25 | xyz | — |
| `Ab-L/R-Thigh-Tw0`（大腿扭转骨） | 1 | 300 | 30 | xyz | — |
| `Ab_Acc_ForearmL/R`（护腕） | 0.5 | 400 | 30 | xyz | x、z |

- 全部 `bLimitDisplacement`、`ErrorResetThresh 255`、`Alpha 1`。
- 剑星改过这个节点：多了 `bUseLocalSpace`（全是 true）、`BaseSpaceBoneName`（全是 None）、`AverageVelocityFrameCount`（4）、`HistoryBoneVelocity`。
  源码在游戏的 exe 里，读不到；这里按字面意思理解成"在角色自己的空间里模拟"（走路、被打退不带动，只有身体自己的动作带动），速度平均没有照做。
- 算法照 UE 4.26 的 `AnimNode_SpringBone`：固定 120 Hz 子步，误差 × 刚度 − 速度 × 阻尼；阻尼大于 1/dt 时按比例缩小；
  每步速度不超过 `ErrorResetThresh`；位移限制在 `MaxDisplacement` 的球里；旋转按允许的轴取父骨→骨的偏转。

### 2.4 KawaiiPhysics（9 个节点）

剑星用的 KawaiiPhysics 比 Fiona（Vindictus 2024）的老一代：有 `TeleportDistance/RotationThreshold`，还没有模拟空间、骨骼约束这些字段；
另外有剑星自己加的 `EnterVehicleScale`、`MasterMesh*Limits`（都没用上）。核心算法相同，直接用 Fiona 时移植的 `RoeKawaiiPhysics`。

| 节点 | 根骨 | 排除 | 阻尼 | 刚度 | 半径（cm） | 碰撞 |
|---|---|---|---:|---:|---:|---|
| 主蓝图 | `Ab-TL-HairB01`（马尾根） | `Ab-TL-HairB03` 及以下 | 0.1 | 0.05 | 3 × 曲线 `KPhysics_Dn_Cv01_2` | 21 个胶囊（脊柱、手臂、腿、头、锁骨） |
| 头发 ×4 | `Ab-HairVFLL01` / `VFL01` / `VFR01` / `VFRR01`（前发） | — | 0.1 | 0.05 | 1 | `KawaiiLimitsData_Head`：头部 2 个胶囊（挂在头发网格的根骨） |
| 头发 ×2 | `Ab_Hair_exL01` / `exR01`（鬓发） | — | 0.1 | 0.05 | 1 | 同上 |
| 衣服 ×2 | `Ab_NeckAcc01`（排除 04）、`Ab_NeckAcc03`（领带上下两段） | — | 0.1 | 0.05 | 3 | 6 个胶囊（脊柱、上臂） |

- 全部 `WorldDampingLocation/Rotation 0.8`、重力 0、目标帧率 60。
- 风（`bEnableWind`、`WindScale`）没做：擂台上没有风。
- 改了一处：节点改成逐个模拟、逐个写回（UE 动画图就是一个接一个跑的）。领带两段节点首尾相接，后一段的根要先看到前一段的结果；Fiona 的节点互不相干，不受影响。

### 2.5 PhysX 刚体链

马尾：`Ab-TL-HairB01`、`B02` 交给上面的 KawaiiPhysics（它排除了 `B03` 及以下），从 `B03` 往下 7 节是刚体（马尾网格自己的物理资产）：

| 骨骼 | 质量（kg） | 线阻尼 | 角阻尼 | 关节（锥角 1 / 锥角 2 / 扭转） |
|---|---:|---:|---:|---|
| `B03` | 0.6 | 5 | 1 | 45° / 45° / 15°（连 `B02`） |
| `B04` | 0.5 | 5 | 1 | 45° / 45° / 15° |
| `B05` | 0.3 | 5 | 1 | 同上 |
| `B06` | 0.1 | 5 | 1 | 同上 |
| `B07` | 0.08 | 5 | 1 | 同上 |
| `B08` | 0.06 | 5 | 1 | 同上 |
| `B09` | 0.04 | 5 | 1 | 同上 |

（45° 是 UE 的默认值：无版本序列化不写默认值，读不到就是默认。）碰撞体：这套衣服的马尾碰撞集（头球 + 头胶囊、脖子、脊柱、骨盆、大小腿、上臂、前臂）加上马尾自己的 `B01`、`B02`。

披风：左右各 7 节 `Ab_CapeL/R01..07`，挂在上臂扭转骨 `Ab-L/R-UpperArm-Tw1` 下面；每节 20 kg、线阻尼 5、角阻尼 10，关节锥角 10° / 10°、扭转 10°；
碰撞体是这套衣服物理资产里身体各骨的运动学刚体（领带、胸、臀也在里面）。

搬到 Unity：

- 每个角色一个**自己的物理场景**（播放时 `SceneManager.CreateScene(..., LocalPhysicsMode.Physics3D)`，编辑器里录像时用预览场景），
  刚体放在角色自己的坐标系里，所以等于游戏的 `bLocalSpaceSimulation`：人整体移动不甩，身体动作才甩。格斗每步走一次 `PhysicsScene.Simulate`。
- 运动学刚体每步移到骨骼当前的位置（`MovePosition` / `MoveRotation`），模拟完把刚体的旋转写回骨骼。
- 关节用 `ConfigurableJoint`：主轴 = UE 的扭转轴，副轴 = UE 的 Swing2 轴，第三轴 = Swing1；位移全锁；投影 5 cm（UE 约束的默认）。
- UE 物理资产编辑器会把"静止时就碰在一起"的刚体对排除掉（存在碰撞禁用表里，读不到），这里在建的时候的姿势上用 `Physics.ComputePenetration` 找出来排除（绑定姿势 20 对，格斗里按站架建，19 对）。
- 每个关节按游戏的约束框架（Frame1 在子骨空间、Frame2 在父骨空间）把子刚体摆到静止位置再建关节，所以关节的零点就是游戏的静止姿态。

轴向：UE 骨骼空间的 (x, y, z) 是她骨骼里的 (−x, −y, z)（和 Fiona 一样），检查结果：

> 21 个关节，游戏约束框架算出的子骨静止位置和她骨骼的实际位置：全部 0.0 厘米、0 度。

### 2.6 没搬的

| 东西 | 是什么 | 为什么没搬 |
|---|---|---|
| `BtoB_CtrlRig` | 衣服蓝图里的 Control Rig（RigVM，12 个函数：取骨骼变换、向量长度、浮点重映射、累加插值、设变换），每套衣服都有 | 量上臂、前臂、手到胸的距离，手臂压到胸时把胸骨挤开（`tools/sb_controlrig.py` 反汇编到 `_work\sb\research\btob_ctlrig.txt`）。它推的是衣服自己的胸骨，这里衣服和身体共用一套骨，推了连身体一起动；三个距离阈值是蓝图引脚给的变量，还没读。第 6 节的虚拟机能跑它，要做时补上累加插值和三个向量单元 |
| 衣服上的 NvCloth 布料 `CH_P_EVE_09_skin_Eve01_AX1_L_Clothing_0` | 网格布料（APEX 那一套） | 只是领口附近很小的一片 |
| 马尾组件 `Root` 上的形状 | 马尾网格的根骨（挂在头的插槽上）上一个头部胶囊、衣服碰撞集里一块 7.5 cm 厚的大平板 | 这个根骨不是身体的骨骼，位置要从马尾骨架换算；头部已有衣服碰撞集里的头球和头胶囊。那块平板看起来是挡马尾甩到身前的，后面要是看到马尾穿到身前再加 |
| `PhysTransformStabilizationLerpValue 0.2` | 剑星自己加的组件参数 | 源码在 exe 里 |
| 扭转骨、修正骨（`Ab-*-Tw0/1`、`Ab-*-Elbow`、`Ab-*-Knee`、`Dm-*` 等） | 3ds Max 的辅助骨 | 游戏里是逐帧打在每段动画上的关键帧，不是运行时算的（主蓝图里没有驱动它们的节点）。人形动作不带这些骨，所以要像 ROE 角色那样用她游戏里的动作片段拟合（`RoeHelperFit`），等导她自己的动作时一起做 |

## 3. 脚（10-05 修）

用户问："eve的脚是不是有问题"。对比图 `out\eve09_feet_fix.jpg`（上：修复前，下：修复后；站架、前进、侧步、刺拳收招，贴地拍）。

- **她的脚**：坡跟厚底靴，脚在鞋里绷到 77°（脚踝到脚掌只往前 2.6 厘米、往下 11.3 厘米），和小腿只差 16°；
  绑定姿势下整块鞋底平贴地面（从脚踝后 8 厘米到前 6 厘米都着地），脚踝离地 15.3 厘米、脚掌骨 4.1 厘米。Blender 里量的。
- **出了什么错**（`RoeFootProbe.FootFrames` 分三步量）：
  1. 绑定姿势：脚尖朝前（左右各外撇 10°），鞋底贴地。
  2. 动捕站架直接套到她的人形骨架上：左脚尖转到朝后 157°、右脚 129°，脚踝抬到 30 厘米，鞋底斜着。
     动捕演员站架时脚跟抬起（脚往下绷），加到她已经绷直的脚上，就越过垂直线翻到后面去了。
  3. 格斗逻辑把第 2 步的样子当成"站着时脚该什么样"（`footRest`），每一帧照着摆。ROE 角色也有类似的翻转，
     但它们的这个样子取自游戏自己的站立动作，所以能纠正回来；Eve 没有游戏动作，取到的就是翻过去的动捕站架。
- **改法**（`tools/sb/eve09.json` 的 `"feet": "bind"`）：
  - 脚和脚趾相对小腿一直保持绑定姿势的角度（`FighterRig.bindFeet`）。她的脚在鞋里本来就动不了；踢腿、迈步时也不会翻。
  - 落地的脚照绑定姿势站：鞋底放平，朝向跟着小腿（脚踝的转轴），按绑定姿势的脚踝和脚掌高度落地
    （`flatFeet` 那一套，`flatRest` / `flatToe` / `flatSoles` 取自绑定姿势，不再转脚；建场景时 `RoeFightScene.BindFeet` 量）。
  - 借来的"游戏动作"（出场、受击、技能，都是 UFE 2 的）格斗逻辑原本不做落地，套到她身上整个人陷进地里 13.7 厘米。
    现在她身体立着时（头到胯的方向和竖直方向夹角小于约 50°），鞋底低于地面就整体抬上来：只抬不压，跳起来的招保持高度，躺倒时不动。
- **结果**（`RoeFightProbe.Feet`，64 个镜头）：站架、走、退、侧步、四个攻击里落地的脚，鞋跟一半和鞋尖一半离地都是 0.0 厘米，朝向：
  前脚内扣 20°、后脚外撇 64°（格斗站架本来的样子）；出场动作鞋底在地面下 0.6 厘米。录了一场电脑对电脑看受击、技能、被 KO、躺地，脚都在地上。

## 4. 效果

物理对比录像，每段 11.6 秒：站架、前进、后退、两次侧步，再按 A、C、D、B 四个攻击（刺拳、直拳、回旋踢、挥砍）；左：剑星自己的（`stellar`），中：我们的骨骼布料（`magica_style`），右：关。

- `out\eve09_physics_front.mp4`：正面全身；
- `out\eve09_physics_back.mp4`：背后全身（演示工具新加的 `-roeView back`；原来的 `backright` 只拍胯部特写，看不到整条马尾）。

看到的：

| | 剑星自己的 | 我们的骨骼布料 | 关 |
|---|---|---|---|
| 马尾 | 走路时贴着背小幅摆；回旋踢、转身时整条甩出去，再落回背后 | 基本直直挂着，踢腿、转身也几乎不甩（按头发的预设，刚度大） | 直直垂在背后，只跟着头走 |
| 披风 | 自然垂下，跟着腿和胯摆，踢腿时被带起来 | 没认出来，垂在身体两侧不动 | 垂在身体两侧不动 |

胸、臀、大腿、护腕的弹簧骨，游戏设的位移上限只有 0.5–2 厘米，画面上几乎看不出来。

电脑对电脑 `out\fight_cpu_match_eve09.mp4`：对 a08，120 秒（修脚之后重录）。抽查整场的画面，马尾和披风没有甩飞、没有卡在奇怪的位置。
画面里大团的黑灰色拖影是命中火花（所有对局共用的 g04 技能命中特效），不是她的物理。

**录这组视频时发现并修掉的问题**（所有角色都有）：换物理方案或开新的一局时（F4、F3、选人后再开一局，`FighterRig.Init`），
新方案把骨骼当时的样子记成静止姿势，可旧方案没先把它动过的骨骼放回去。第一次录的视频里，骨骼布料那栏的马尾从剑星物理最后一帧的样子开始，
一直斜着支着；"关"那栏停在骨骼布料最后一帧的样子；exe 里按 F4 后马尾一直水平支着。现在 `Init` 一开始先让旧方案把骨骼放回静止姿势。
修复后重录：第一栏（剑星自己的）和修复前逐像素相同，后两栏的马尾变成直直垂着，就是上表。

## 5. 用法

```powershell
# 1. 模型（Blender 3.6，读归档的 .blend，不保存）
& 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup `
    'E:\game_export\StellarBlade\Eve\blend\CH_P_EVE_09\Eve_CH_P_EVE_09.blend' --python tools\sb_fbx.py -- Assets\SB\eve09 eve09
# 2. 贴图（游戏材质实例 → URP 的图，结果 unity.json）
python tools\sb_textures.py Assets\SB\eve09
# 3. 物理（游戏的蓝图、物理资产 → kawaii.json、sbphysics.json）
python tools\sb_physics.py Assets\SB\eve09
# 4. Unity：建角色，再重建对战场景
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','eve09'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
# 物理对比录像：剑星自己的 | 骨骼布料 | 关
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','eve09','-roeCloths','stellar,magica_style,off','-roeView','back','-roeOut','E:\code\othercode\roe_fighter_unity\_work\eve_cloth_back'
python tools\cloth_demo_video.py _work\eve_cloth_back out\eve09_physics_back.mp4 --chars eve09 --variants stellar,magica_style,off
# 电脑对电脑
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Record -Graphics -Extra '-roeP1','eve09','-roeP2','a08','-roeSeconds','120','-roeOut','E:\code\othercode\roe_fighter_unity\_work\fight_eve09'
python tools\make_video.py _work\fight_eve09 out\fight_cpu_match_eve09.mp4
# 脚：贴地拍并量鞋跟、鞋尖离地多高；脚的朝向分三步量
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightProbe.Feet -Graphics -Extra '-roeChar','eve09','-roePacks','bandai1'
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFootProbe.FootFrames -Extra '-roeChar','eve09'
```

- 素材在 `Assets/SB/eve09/`（不入库），格斗定义 `tools/sb/eve09.json`。
- 游戏包里读的东西只缓存在 `_work\sb\`。
- 换别的衣服：`sb_fbx.py` 换 `.blend`，`sb_physics.py --outfit CH_P_EVE_xx`，`sb_textures.py --mi-dir Art/Character/PC/CH_P_EVE_xx/Materials`。

## 6. 百褶裙（eve37）和裙子的 Control Rig（10-05）

用户 10-05："eve有穿jk的衣服么，换成这个角色我看下裙子的物理效果"。

- 她没有真正的 JK 制服：Daily Sailor（`CH_P_EVE_05`）是水手领上衣配牛仔裤。带百褶裙的是 **Office Style**（`CH_P_EVE_37`）：
  白衬衫、黑领带、灰色百褶短裙（前面两排扣子）、丝袜、白色厚底高跟鞋。做成了新角色 `eve37`，选人界面里有，默认名单也加上了。
- 建法和 eve09 一样（第 5 节的命令），换成 37 的 .blend、材质目录和 `--outfit CH_P_EVE_37`；脚同样是 `"feet": "bind"`（鞋里的脚也绷到 77°）。

### 6.1 游戏里这条裙子怎么动

衣服的动画蓝图 `CH_P_EVE_37_AnimBP`，按节点之间的姿势连线（`LinkID`）排出的执行顺序：

1. `BtoB_CtrlRig`（每套衣服都有，手臂压胸）——没搬，见 2.6；
2. **`CH_P_EVE_37_Skirt_CtlRig`**：裙子的 Control Rig，按大腿抬起多少把各裙片的根骨转开；
3. 10 个 KawaiiPhysics 节点：裙片 D、O、E、Q、S 左右各一，每个节点的根骨是该片的第 2 节（`Ab-L-SkirtE-02` 等），带腿上的碰撞胶囊；
4. 领带、左右两根绳（`Ab-L/R-StringC-01`）的 KawaiiPhysics。

以前 `sb_physics.py` 照类里列出节点的顺序写 kawaii.json，那不是执行顺序。现在照连线排（`graph_order`）：
eve09 的顺序没变，eve37 变成上面这样，rig 在所有裙片的 Kawaii 之前。

**rig 做的事**（字节码解出来的，`tools/sb_controlrig.py` 反汇编，16 种单元、305 步、没有分支）：

- 先量两条腿"抬了多少"：`Dm-L-Thigh-point`（挂在骨盆上、和大腿根同一个点的辅助骨）相对大腿扭转骨 `Ab-L-Thigh-Tw0` 的变换，
  旋转换成 UE 的 Rotator（俯仰 Pitch、偏航 Yaw、滚转 Roll，单位度）；D 片用的是相对大腿 `Bip001-L-Thigh` 的。
  下表里"左偏航""左滚转"指左腿这个相对旋转的分量，"左平移 Z"指相对平移的 Z（厘米）。扭转骨是弹簧骨，所以腿动得快时平移不是 0。
- 再在各片根骨（`-01`，Kawaii 链根骨的上一节）自己的局部空间里加上去（右侧把左右对调；"取正"= 小于 0 时取 0）：

| 裙片 | 角度 | 位置 |
|---|---|---|
| E | 滚转 + 取正(0.6 × 左偏航)（右片用 −右偏航） | 不变 |
| Q | 滚转 + 取正(左滚转) + 0.5 × 取正(右滚转) | 局部 Z 取两侧中较小的（本片 Z + min(−本侧平移 Z, 0)），左右两片同一个值 |
| S | 俯仰 − 取正(0.8 × 左滚转)（右片 +）；滚转 + 取正(0.7 × 左偏航) + 取正(0.8 × 左滚转) | 同 Q 的算法再乘 1.1 |
| O | 偏航 − 0.3 × 左滚转；滚转 − 0.3 × 左滚转（右片的滚转是 +），不取正 | 不变 |
| D | 偏航 + 重映射(左腿滚转 0..95 → 0..−7) + 重映射(右腿滚转 0..95 → 0..7)，即 7/95 ×（右腿滚转 − 左腿滚转）：两腿差 95° 时 7° | 不变 |
| 左绳 StringC | 不变 | 局部 Z = 0.5 × min(本节 Z + min(−左平移 Z, 0), 0) |
| 骨盆 | 不变 | 局部 Y + 1.2 × 两侧 −min(−平移 Z, 0) 之和，不带子骨 |

D 片的第二个重映射有没有钳制，接的是"左腿滚转 ≥ −0.4406 且 右腿滚转 ≤ −0.4406"这样一个比较（右片反过来），像是调试时留下的，照搬。

### 6.2 怎么搬过来的：直接跑它的字节码

不是照着上表手写一遍，而是写了一个小虚拟机把 rig 原样跑起来：

- `tools/sb_controlrig.py --json`（`sb_physics.py` 调它，结果写进 `sbphysics.json` 的 `rigs`）：把 RigVM 的三块内存（运算、字面量、变量）
  展开成一列浮点数（变换 10 个：旋转 xyzw、平移 xyz、缩放 xyz；四元数 4；Rotator 3；向量 3；浮点、布尔 1），
  每条指令写成"单元名 + 每个引脚的（寄存器、从第几个数、几个数）"；成员路径（`Rotation`、`Translation.Z`、`Roll`……）换成偏移。
- `RoeRigVM`（C#）照顺序执行：`Copy` 拷一段数，其余是 UE 4.26 同名单元的实现（`FTransform` 的乘法和求逆、`FQuat::Rotator`、
  `FRotator::Quaternion`、`FloatRemap` 等）。不认识的单元直接拒绝加载，不会悄悄算错。
- 骨骼按 UE 的样子给它看（`RoeRigVM.UnityBones`）：骨骼局部轴是她的绕 z 转半圈（x、y 取反）；组件空间是格斗者自己的坐标系
  （UE 的 x、y、z 是我们的 z、x、y）；米换厘米。rig 写回局部变换，Unity 的子骨自然跟着走（和 UE 输出局部姿势的结果一样）。
- 在 `RoeSbPhysics` 里的位置和游戏一样：弹簧骨之后、Kawaii 之前。rig 是"读当前值再加"，所以它设的骨每帧先放回绑定姿势
  （和其他物理骨一起在 `Rest` 里），否则会一帧帧累加。
- 骨盆不让它动：游戏里衣服是单独的网格，有自己的骨盆，挪的只是衣服；这里衣服和身体共用一根骨盆，挪了整个人都会动。
  它想挪多少记在报告里（`asked Bip001 Pelvis ... for x cm`）：录像的 11.6 秒里最多 2.2 厘米（扭转骨是弹簧骨，腿动得快时落在大腿根后面）。
- 格斗逻辑里有一条给 ROE 角色用的规则：动捕时把挂在骨盆上的裙片按"站架时相对胯部朝向的角度"重新摆，因为 ROE 的裙子是逐帧手 K 的、
  站架的骨盆是歪的（`FighterRig.hipCloth`）。以前它也作用在 Eve 身上：eve37 的 Q、S 片和两根绳的根骨，eve09 的大腿辅助点
  （只挂着不蒙皮的辅助骨，看不出来）。剑星的裙子不是手 K 的，rig 要读的正是动画给的位置，所以 `stellar` 方案现在不用这条规则
  （`RoeClothSolvers.Solver.ownSkirt`）。

**两项检查**（建角色时 `SbFighter.AttachSb` 跑，结果在日志里）：

1. **重放**：游戏包里存着编辑器最后一次运行 rig 后整块运算内存的值（每个寄存器都是它的单元用别的寄存器算出来的）。
   把这些值当输入，除了读写骨骼以外的 305 步全部重算一遍，和存的值比：最大差 0.000125（存的是 6 位小数的文本）。
2. **轴向**：rig 资源里带着它自己那份骨架的参考姿势。拿她的绑定姿势换算过去比：18 根相关骨骼的全局变换全部 0.00 厘米、0.00°；
   rig 按局部读写的骨骼，局部变换也是 0。

### 6.3 效果

物理对比录像，每段 11.6 秒，动作和 eve09 那组一样（站架、前进、后退、两次侧步，再按 A、C、D、B：刺拳、直拳、回旋踢、挥砍）；
左：剑星自己的（Kawaii + Control Rig），中：剑星的 Kawaii 但不跑 Control Rig（演示工具的 `stellar_norig`），右：我们的骨骼布料（`magica_style`）。

- `out\eve37_skirt_back.mp4`：背后全身；
- `out\eve37_skirt_hips.mp4`：侧前方的胯部特写（新加的 `-roeView frontright`；从背后拍的特写被马尾挡住）；
- `out\eve37_skirt_kick.jpg`：回旋踢抬腿那几帧放大（8.9–9.2 秒；每行左 rig、中不跑 rig、右我们的）。

看到的：

- 剑星自己的两列大部分时间几乎一样：胯部特写逐帧比，平均只有 1.7% 的像素不同，多数是马尾（PhysX 刚体链每次跑都不完全一样）。
  差别集中在踢腿抬大腿的时候：rig 把那条腿上方的几片裙子跟着大腿转开（这一段里最多 37°），整片掀起来；
  不跑 rig 时，裙片压在抬起的大腿上，只靠 Kawaii 的碰撞胶囊往外推。
- 剑星的裙子摆得开：站架时下摆就往外张，出拳、转身时下摆甩成一圈（重力、阻尼、腿上的碰撞胶囊都是游戏的值）。
- 我们的骨骼布料：裙子贴着腿往下垂，甩得少；踢腿时被大腿顶起来，盖在大腿上。
- rig 想挪衣服的骨盆，这一段里最多 2.2 厘米，这里跳过了。

### 6.4 用法

```powershell
& 'D:\Program Files\blender-3.6.15-windows-x64\blender.exe' -b --factory-startup `
    'E:\game_export\StellarBlade\Eve\blend\CH_P_EVE_37\Eve_CH_P_EVE_37.blend' --python tools\sb_fbx.py -- Assets\SB\eve37 eve37
python tools\sb_textures.py Assets\SB\eve37 --mi-dir Art/Character/PC/CH_P_EVE_37/Materials
python tools\sb_physics.py Assets\SB\eve37 --outfit CH_P_EVE_37          # 连同裙子的 Control Rig 程序
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.SbFighter.Build -Graphics -Extra '-roeSb','eve37'   # 日志里有重放和轴向检查
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeFightScene.Build -Graphics
# 对比录像：剑星自己的 | 不跑 rig | 我们的骨骼布料（另一组 -roeView back）
.\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeClothDemo.Run -Graphics -Extra '-roeChars','eve37','-roeCloths','stellar,stellar_norig,magica_style','-roeView','frontright','-roeOut','E:\code\othercode\roe_fighter_unity\_work\eve37_rig_frontright'
python tools\cloth_demo_video.py _work\eve37_rig_frontright out\eve37_skirt_hips.mp4 --chars eve37 --variants stellar,stellar_norig,magica_style
# 看 rig 的指令
python tools\sb_controlrig.py _work\sb\research\json\SB\Content\Art\Character\PC\CH_P_EVE_37\CH_P_EVE_37_Skirt_CtlRig.json _work\sb\research\skirt_ctlrig.txt
```

- 素材在 `Assets/SB/eve37/`（不入库），格斗定义 `tools/sb/eve37.json`；rig 的程序在 `sbphysics.json` 的 `rigs` 里（`RoeSbRig.Data.rigs`）。
- 想关掉 rig 看区别：演示用 `stellar_norig`；代码里是 `RoeSbPhysics.UseControlRigs`。

## 7. 粉色紧身衣（Cooling Suit，eve_nikke06）（10-17）

- NIKKE 联动 DLC 的服装在游戏包的 `SB/Content/DLC_2/Art/Character/PC/<服装>/` 下：贴图用 `--mi-dir DLC_2/Art/Character/PC/CH_P_EVE_Nikke_06/Mat`，
  物理用 `sb_physics.py --dlc DLC_2`（只有服装自己的动画蓝图、物理资产、马尾碰撞改路径，主蓝图和头发还是本体的）。
- 材质实例可以把父材质的混合方式覆盖掉（`BasePropertyOverrides`）。这套腿上的字（`MI_VietaCos004_Decal`）覆盖成了遮罩、裁剪阈值 0.333：
  `sb_textures.py` 现在读这个覆盖，保留颜色图的 alpha、按阈值裁剪，不再是一块黑方块。
- 爆衣、录像、命令见 README"剑星 Eve 的粉色紧身衣"一节。

## 8. Alice 的发型、她自己的动作和剑（eve_nikke06，10-17）

- 发型换装：`tools/sb_hair.py` 在 `sb_fbx.py` 之前跑（同一个 Blender 会话）。去掉默认发型和只有它用的骨，导入新发型的 .psk，按头骨的静止矩阵对齐后合进骨架。
  新发型的材质透明度用它自己的 `*_ADIR` 图（红通道）；`sb_physics.py --hair-dlc` 读 DLC 里发型的蓝图；没有马尾组件就没有刚体马尾。
- 动作：`tools/sb_anims.py` 导出 .psa（CUE4Parse），`SbAnims` 转成人形动画。读法是"Y 镜像 + 子骨共轭"；组件空间 UE (x, y, z) → 她的 (y, z, x)；
  CUE4Parse 的帧率是帧数 / 时长，间隔取时长 / (帧数 − 1)。ACL 压缩的片段（角色界面、Social）导不出来。
- 剑：`CH_W_Sword_01`，游戏让剑的根骨 `Weapon_Root` 与 `Bip001-Prop1` 重合；Prop1 挂在 Bip001 下，人形动画里没有它，
  所以剑挂在右手下的持剑点 `SB_Sword`，每个动画按游戏里 Prop1 相对右手的位置给持剑点打关键帧（技能里剑离手也照游戏）。
- 招式、技能、录像、命令见 README"Eve 粉色紧身衣：Alice 的发型、她自己剑星里的招式"一节。
- eve09、eve37 同一天也换成了这套招式（`tools/sb_own_moves.py`，每套用自己的动画副本），见 README"eve09、eve37 也用她自己剑星里的招式"一节。

## 9. 渡鸦（Raven，`raven`）（10-18）

- 剑星里的 Boss，`SB/Content/Art/Character/Monster/CH_M_NA_53`。模型来自 ripper_tpose 的 `scripts/stellarblade/npc`（身体、脸、头发三个网格各一副骨架）：
  `tools/sb_parts.py` 在 `sb_fbx.py` 之前把三副骨架并成一副（同名骨用身体的，头发多出的 25 根照加）。
- 她只有一个动画蓝图和一个物理资产：`sb_physics.py --abp <蓝图> --physics <物理资产>`。6 根弹簧骨、5 个 KawaiiPhysics 节点（背后尾摆、前后飘带），
  长发是 130 个刚体挂在 `HBN_*` 发束骨上（物理资产里非运动学的那些），身体的 43 个运动学刚体当碰撞体；链名 `hair`，算头发。
- 武器 `CH_M_NA_53_Weapon`：骨架里的插槽 `SC_Weapon` 挂在 `Bip001-Prop1` 上，转 Pitch −90 / Yaw 180 / Roll −180，刃（网格的 z）落在 Prop1 的 x 上，和 Eve 的剑一样；
  `weapons.json` 的 `socketRotator` 记这个转角。
- 动作：她自己的 83 个动画（`sb_anims.py --folder .../CH_M_NA_53/Animation`），`SbAnims -roeSrc <目录> -roeCalib M_Raven_BattleIdle01`；读法同 Eve。
- 爆衣：游戏自带的 `CH_M_NA_53_1` 就是她战衣下的完整身体，当裸体底身（`SbFighter.BuildBase -roeSb raven_nude`）。
- 战衣的白色乳胶在游戏里有一层清漆（`ClearCoatRoughness Min` ≈ 0）和薄膜干涉光泽。URP Lit 没有清漆，`sb_textures.py` 在清漆遮罩（`ShadingModelMask` 的绿通道）覆盖的地方把光滑度抬到至少 0.9，
  不然米白色的乳胶看起来像皮肤。
- 详细见 README"剑星的渡鸦（Raven）加入战斗"一节。
