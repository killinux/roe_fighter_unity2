# Rise of Eros 的战斗里有没有物理（10-05）

用户 10-05："战斗的场景也没有物理是吧，都调研一下，ROE内的"。

## 结论

**战斗里没有物理模拟。** 角色、敌人、舞台、技能都没有刚体、关节、布料组件，也没有 Magica Cloth。
- 角色身上会动的东西（头发、胸、裙子、挂件）全是关键帧。
- 舞台里只有两类和"物理"沾边的东西：
  - 不会动的碰撞体：给点击、手势的射线检测用，也给粒子当地面；
  - 少量粒子特效：落叶落到地面、风吹沙子。

真正的物理（Magica Cloth 2）只在大厅里：头发、挂件、时装配件，还有 Lynn 的高级 H 场景。

## 怎么查的

`tools/roe_physics_scan.py` 读游戏包（只读，安装目录和下载目录里同名的取较新的），每个包里找：

- Unity 自带的物理组件：刚体、各种碰撞体、关节、Unity 自己的布料（Cloth）、风区、恒力、角色控制器（含 2D 的）；
- 脚本：Magica Cloth 2 的组件，以及名字像物理的（spring、jiggle、dynamic bone、sway、wind、ragdoll、rope、chain、physics、cloth……）；
- 开了碰撞或外力的粒子系统；
- 顺带数动画组件和动画片段。

战斗会加载的几类包全部扫了，一共 5998 个，22 秒：

| 包 | 个数 | 有物理的 | 找到的 |
|---|---|---|---|
| `chara_armor_*` 战斗、展示角色 | 266 | 0 | 248 个动画组件、1290 个动作；没有任何物理组件 |
| `chara_enemy_*` 敌人 | 115 | 0 | 89 个动画组件、695 个动作 |
| `gameplay_skill_*` 技能数据 | 231 | 0 | 纯数据 |
| `scene_battlefield_*` 战斗舞台 | 194 | 125 | 静止碰撞体：网格 3511、球 168、盒 130、胶囊 82，没有一个刚体；粒子系统 2769，开碰撞的 47、开外力的 79；力场 45 个 |
| `scene_bind_battlefield_*` 舞台的后期设置 | 182 | 0 | 只有画面后处理 |
| `env_level*` 舞台的环境模型 | 1427 | 111 | 同舞台：静止碰撞体，粒子开碰撞 34、开外力 77 |
| `vfx_*` 特效 | 3058 | 18 | 粒子系统 46379，开碰撞的 107（大多是回忆模式里 H 场景的液体），盒碰撞体 22（液体溅到身上的贴花） |
| `accessory*` 配饰 | 190 | 12 | 时装配件上的 Magica BoneCloth 21 个（头纱、蝴蝶结、恶魔翅膀、牛尾巴、流苏、耳环……）；Unity 胶囊碰撞体 75 个（`CLD_*`，挂在衣服、内衣上） |
| `meta_armor_*` 大厅（对照） | 264 | 84 | MagicaCloth 95、Magica 碰撞体 653、风区 1 |
| `meta_bare_*` 大厅裸体（对照） | 71 | 57 | MagicaCloth 38、Magica 碰撞体 354、每个包一个 Unity 球碰撞体 |

几点说明：

- **舞台的碰撞体都是静止的**：挂在 Quad、Plane、Cube、Cylinder、`TerrainBox` 这类不可见的面片和方块上，没有刚体，所以没有东西会被物理推着动。
  - 舞台的镜头上有 `PhysicsRaycaster`，舞台里有手势控制器（`FingerGestureController`、`GestControllerInitializer`）：这些碰撞体是给点击和手势做射线检测的。
  - 舞台里会动的布、旗子之类，用的是动画（舞台里有 171 个动画组件）。
- **粒子**：
  - 开碰撞的主要是 `FX_Leaves`（落叶，碰的是平面，落在地上停住）；
  - 开外力的是 `FX_Sand_Blowing`（被 45 个粒子力场吹动的沙子）；
  - 技能特效里开碰撞的只有零星几个（`sandpa`、`sandsmoke`、`rock`），其余是回忆模式里的液体。
- **时装配件上的 Magica**：
  - 这些时装没有战斗模型，战斗模型里的时装只有 4 套泳装，而且也没有物理，所以应该只在大厅里用。
  - 配件上的 Unity 胶囊碰撞体（`CLD_OpenVest`、`CLD_LacePanties` 等）没有刚体，看起来是给点击、触摸用的。
- 名字像物理但不模拟的已经单独列出、不算在内：`PhysicsRaycaster`（点击射线）、`MaterialPropertyLooper`（材质动画，名字里有 "rope"）。

## 和格斗的关系

- Inase 的裙子、头发、胸在游戏里全是关键帧（`docs/inase-skirt.md`）。格斗里用游戏动作时直接播这些关键帧；动捕动作没有这些部位的关键帧，所以要用物理（骨骼布料、Magica Cloth 2，或者各角色自己游戏里的那一套）。
- 舞台在格斗里只当背景用，它本来也没有物理。

## 命令

```powershell
python tools\roe_physics_scan.py                  # 扫（结果追加到 _work\roe_physics_scan\scan.jsonl，可以接着扫）
python tools\roe_physics_scan.py --report         # 按类别汇总
python tools\roe_physics_scan.py chara_armor_pc_a08            # 只扫某些包名开头的（已扫过的跳过）
```
