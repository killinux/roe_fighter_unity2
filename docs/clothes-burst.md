# 实时爆衣：调研和方案

> 2026-10-03。问题：ROE Fighter（Unity 6000.4 + URP，a08 Inase 对 g04 Luffee）里怎么在**比赛进行中**做爆衣。
> 之前在 ripper_tpose 里做的是 Blender 离线版（布料撕裂、爆衣两个插件，调研见 `docs/clothes-burst-survey.md` 和 `research_notes/爆衣效果调研/`），这次只看运行时。
> 调研时只做了阅读和分析，没改代码、没开 Unity / Blender（之后的实现见下一节"进展"）。分析脚本在 `tools\research\clothes_burst\`（只读游戏和导出的文件），图在 `out\clothes_burst\`（游戏模型画出来的图，不进仓库）。
> 同一天还拆了 DOA5LR / DOA6 的数据，看它们的衣服和布料怎么做（ripper_tpose `docs/doa-clothing-and-cloth.md`），结论并进了下面的表。
>
> 标记：【本地】读过 / 量过本机的文件；【网页】打开原网页读过；【摘要】只看到搜索摘要；【推断】我的推理。

## 进展（10-03 下午）

**用户定的规则**（"按建议"，对应"要你定的事"一节）：
- 先只做第一步：掉外层，不补身体；
- 超必杀的最后一下打中谁，谁掉下一段；谁被 KO 也掉下一段；
- 整场比赛不恢复；
- 护甲飞出去，烧边以后再做；
- 按固定顺序掉。

**第一步做完了。** 做法、两人各掉什么、怎么给别的角色加，都写在 README 的"爆衣"一节。

和下面"建议的实施步骤"第 1、2 步比，改了两处：

1. **每组一个单独的蒙皮渲染器，不是"每组一个子网格 + 材质实例"**：
   - 藏起来就是关掉这个渲染器，不用改材质、不用 `_IGNOpacity` 归零；
   - 藏起来的部件不再走四个 pass（原方案是照画再在像素里裁掉）；
   - 新渲染器和身体用同一套骨头，爆之前和原来逐像素比较，最大差 2/255（后期的抖动噪点）。
   - `_IGNOpacity` 只用在碎片淡出上（`MaterialPropertyBlock`，不建材质实例）。
2. **分组不用调研时的 Python 脚本，在 Unity 编辑器里重新算**（`RoeBurstBuilder`，规则表 `Assets\RoeFighter\Editor\Burst\<id>.json`）：
   - 连通块和主骨的结果和调研一致：a08 body2 485 块，g04 104 块，各组三角形数相同；
   - 每组按左 / 中 / 右再分开，左右两片各飞各的。

量到的数：
- "坑"一节担心 `BakeMesh` 慢，实测一段拍快照 0.5–5.6 毫秒（批处理编辑器里）；
- 加上第一次生成火花特效，整段最多 26 毫秒，正好落在超必杀收招的顿帧或 KO 的慢动作里。

**做的时候发现的问题**：
- 两人的超必杀和技能 2 在游戏技能表里没有"冲上前"那一段，只有 2.9 米内才打得中；
- 电脑原来在 4.5 米内就放，常打空，因而很少因超必杀掉衣服；
- 已改成只在打得到的距离放。

**第二步（补身体）也做了**（用户看了第一步："inase 的爆衣不完全呀，还是先做个 inase 结合 nude 的模型，让爆衣都爆掉……luffee 也是"）：
- 家族裸体底模（`chara_bare_pc_a01_nk*` / `g01_nk*`，只导身体网格、材质、贴图）**整块代替套装皮肤**，按"贴着它的套装皮肤，或皮肤被删处贴身的衣服"转移骨骼权重（`RoeNudeBody`）；
- 用的是下面"建议的实施步骤"第 3 步里说的第二种接缝处理：整张换掉，所以没有接缝；
- 每人四段，决胜的 KO 把剩下的全打掉；
- g04 的鞋一直穿着，鞋里那截底模删掉（高跟角度不同，会穿出来）；
- 顺带发现：套装皮肤和脸的皮肤 LUT 一直没导进工程（在 `chara_tex_bare_common_prelude` 包里），补上以后三者肤色一致。

做法和数字见 README"爆衣"一节。

**10-03 晚加了两个角色**（用户："目前b10，G05也把nude补全，加入战斗"）：
- b10（Kart）用 b01 的底模，g05（Luf 女神）用 g01 的 `fm` 版身体；两者和套装皮肤完全重合（中位 0.00 / 0.01 毫米），套装皮肤就是从它们身上裁的；
- 新问题：套装头部网格只到领子上沿（b10 的高领、g05 的金项圈），衣服掉了脖子是个洞 → 规则表加了 `nude.fill`：从底模的头部子网格补上套装头部没有的三角形（b10 784 个，g05 2,060 个）；
- 两人都能掉到全身，没有一直留着的部件。规则表、检查图、演示见 README"新角色：b10 和 g05"一节。

**10-05 剑星 Eve 的两套**（用户问"爆衣效果目前这几个角色都有么"，之后说"继续"）：
- 衣服下面的身体用归档里的 mod 身体（EveOriginalProportions，赤足）。它和衣服同一副骨架，但比衣服丰满，臀部和胸部会从紧身衣里穿出来，最多 2.8 cm。
- 所以不整块替换后照画，而是被不透明衣服盖住的部分等那件掉了再画（`nude.reveal`）。另外两个开关：
  - 按骨头搬到衣服的绑定姿势（`nude.pose`）；
  - 鞋里贴衣服自己的皮肤（`nude.fit`）。
- 规则表加了按材质分组的条件。
- 做法和数字见 README"Eve 也能爆衣"一节。

**10-17 换色款（b14_outfit1）**：游戏的换色款用原来那套的网格，只换材质。它的爆衣规则不另写，找不到自己的规则就用所属那套的（`RoeManifest.SuitOf`）；b14 的规则里按材质名匹配的地方改成两套都能匹配（`^pc_b14_hd_(outfit1_)?body1$`）。

**10-17 Kart 鹤袍（b14）的右肩**（用户说"继续"，10-11 留下的问题）：
- 全身那段她右肩顶有一道棱：手臂放下时三角肌跟着走，肩顶那一圈不动。
- 原因：长袍下面的套装皮肤一直延伸到右肩关节，游戏看不见，只挂在 Spine2 和锁骨上；再往外，裸体抄的是金色肩饰的权重（挂在它自己的一根骨上）。
- 修法：右肩照左肩镜像取权重（左肩在游戏里露着，权重做得认真），骨头名左右对调，再在这一片上平滑几遍。
  规则表新加 `nude.ownOverSkin`、`nude.ownMirror`；`ownWeights` 的区域边缘按底模挂在这些骨上的比例过渡。
- 对比图 `out\b14_shoulder_fix.jpg`，做法见 README"Crane Kart（b14）拿剑打普通攻击；右肩照左肩取权重"一节。

**10-11 Kart 的鹤袍（b14）**（用户："把kart 最新的模型加入到游戏中"）：
- 底模还是 b01。她的皮肤和 b01 是同一个曲面，只是做得粗一些（沿法线中位 0 毫米）。
- 新问题：套装在右上臂没有皮肤（长袍盖着，游戏裁掉了），盖在上面的纱袖在游戏里挂在前臂扭转骨上。底模抄了它的权重，整条上臂跟着前臂走，站架里横着支出去。
- 规则表加了 `nude.ownWeights`（骨头名的正则）：底模挂在这些骨头上、套装皮肤够不着的顶点，用底模自己的权重。b14 只写右上臂。
  全身都这样做时，大腿上和套装皮肤的权重交界处会出折痕；用 `reveal` 时胸口长袍边缘会露出黑洞。
- 长袍、纱袖、袖子衬里三种材质在接缝处共用顶点，每种材质各写一条 `outfit`，纱袖才能单独掉。
- 规则表、检查图、演示见 README"Kart 最新的一套（b14，鹤袍）加入战斗"一节。

**10-06 衣服里烘焙的影子**（用户说"继续"）：eve37 领带掉了以后衬衫上有一块"黑领带"。原因是游戏把领带的影子烘进了衬衫的 AO 贴图，领带在的时候正好被它盖住。
现在每块会掉的部件，压在别的部件上的那块 AO 会提到周围的水平（`RoeBurstBuilder.CleanOcclusion`），只改下面那块的材质副本。
排查过程、数字和新加的检查开关见 README"Eve 也能爆衣"一节。

**10-06 Fiona（fio005）**（用户："fiona的爆衣先做这个吧，对战的是inase"）：
- 衣服下面用 Vindictus 自己的素体 `SM_Fiona_Body01`：以前没导出过的静态网格，在 Blender 里摆成 PCF_005 的姿势、贴到它自己的皮肤上（`tools\vdf_nude.py`）。
- 规则表新加：素体几个子网格各自的材质（`nude.submeshes` / `nude.materials`）；衣服条目按材质名选子网格（`material`）。
- 镂空材质按三角形算挡不挡身体（覆盖的像素 90% 以上不透明才算，`RoeNudeBody.SolidTriangles`）：Vindictus 的衣服全是镂空材质。
- 做法和数字见 README"Fiona 也能爆衣"一节和 [`vindictus-fiona.md`](vindictus-fiona.md) 第 5 节。

**下一步**（还没定）：
- 第三步打哪破哪；
- 烧边；
- 碎布在空中飘；
- 双面材质的背面打光：URP 的 Lit 不翻背面的法线，衣服里面偏暗（UE 会翻）。可以复制一份 Lit，前向和 DepthNormals 两个 pass 里背面法线取反，双面材质改用它；
- 再加别的角色：照 README"加一个角色的步骤"做，主要的工作是写规则表、看检查图。

## 结论

1. **商业游戏的爆衣几乎都不是实时物理撕裂**，而是"预先做好的几种状态 + 事件触发"（没有一家公开讲过具体怎么画，下面是从官网、报道、补丁说明、mod 资料拼出来的）：
   - 按部位整件打掉或换成破损版：灵魂能力 IV / VI 分上中下 3 个区，海王星 U、Bullet Girls、Queen's Blade 按部位的"服装耐久"；
   - 分阶段：闪乱神乐上下装各 3 段（或 2 段 + 全破），Valkyrie Drive 4 段；
   - 只在 KO 那一下：龙虎之拳 / 拳皇的"脱衣 KO"、灵魂能力 V——和我们这种 KOF 式格斗最接近；
   - 贴图层的脏污、伤痕：DOA6、铁拳 8、真人快打；DOA5LR 的湿身是干 / 湿两张贴图按遮罩混合。
   - DOA6 自己的数据（本机拆的）：能破的衣服各有一个预先做好的 `a` 版整套模型，破坏一击命中时整件换上（428 件里 44 件）。
   触发多是特定招式（灵魂能力 VI 的 Lethal Hit、DOA6 的破坏一击、闪乱神乐的秘传忍法、超必杀）或同一部位的累计伤害，配慢镜头、镜头推近、音效。
   内衣是**单独的一层**：灵魂能力 VI 的内衣基本不会破，闪乱神乐的内衣只有秘传忍法的最后一击才打得掉。详见下一节的表。
2. **我们的素材比预想的好用**（下面"我们的素材能提供什么"全部是【本地】核实）：
   - 服装天然分成互不相连的块：a08 的 body1 服装 8 块、body2 485 块（含链环），g04 104 块。**第一版不用在运行时切网格**，按块分组就能分阶段掉。
   - 外层的块（a08 的长裙片、肩甲、胯甲、链子；g04 的裙片、飘带、腰饰）**下面的皮肤是完整的**（只有 g04 主裙腰上一圈缺 66 cm²），直接拿掉基本不会露洞。
   - 贴身的块（护胫、护手、胸甲、护裆；g04 的上衣、手套、内裤）下面的皮肤被删了：a08 缺 0.376 m²，g04 缺 0.208 m²。
   - **家族裸体底模可以补上**：a08 的皮肤和 a01 底模表面相差中位 0.45 mm（99% 在 3 mm 内），g04 和 g01 底模中位 1.2 mm；两张皮肤贴图在同一位置的颜色差平均只有 1.3–2.1 / 255。但底模身体用到的骨头里，有 53 / 59 根套装骨架里没有（不算头发、脸和私处的骨头），g04 的胸骨名字也不同，所以要在编辑器里做一次权重转移，不能按骨名直接绑。
   - ROE 的战斗服没有任何破损版本，`rip` 动作是"倒地不起"不是撕衣服；但 ROE 的换装部件里有现成的"敞开 / 破洞"状态（比如 g01 的 `StockingsBroken`：同类网格 + alpha 挖洞、洞边画毛边），说明游戏自己做"破"也是贴图挖洞 + 换状态。
3. **推荐路线（分阶段做，每一步都能单独用）**：
   - **第一版：按件掉落**（2.5 天）。编辑器里把服装按块分组、每组一个子网格和材质；爆的时候那组材质 `_IGNOpacity` 归零（着色器原本就有的屏幕门淡出，四个 pass 一起生效），同一帧 `BakeMesh` 拍快照当碎片，在格斗的 60 Hz 步进里自己积分飞出去、落地、淡出，加粒子和音效。只掉外层，两个角色都不用补身体。
   - **第二版：补身体**（2 天）。底模权重转移到套装骨架，按"被哪组衣服盖着"分区，衣服掉了就显示那一区。之后才能掉护胫、上衣、手套、胸甲、内裤。
   - **第三版：打哪破哪**（2–3 天）。着色器里按命中点（绑定姿势空间的球 + 噪声）裁洞，洞边压暗 / 毛边 / 烧边，同时让被裁掉的那块作为碎片飞出去。L4D2 的伤口系统就是"在蒙皮前的姿势空间里用椭球裁像素"（T11）。
   - 可选：裙片像布条一样滑落（骨骼布料链松开）、ROE 自带的内衣部件当最后一层。
   - **不推荐**：运行时按命中切网格 + 物理布料撕裂。Unity Cloth（从 Unity 5 起）和 Magica Cloth 2 都不能撕，Obi 能撕但不支持蒙皮布料（T18–T20）；而且我们的录像和检查是手动一步步推进的，这些插件不好控制。
4. **要你先定的**：做到哪一步（只掉外层 / 拿掉贴身护具 / 全掉）、什么时候掉（血量线 / 技能 / KO）、每回合恢复还是整场保留、护甲是"飞出去"还是"烧掉"。见"要你定的事"一节。

## 别的游戏怎么做

先说清楚：**没找到任何一家（灵魂能力、闪乱神乐、DOA6……）公开讲过爆衣是怎么画出来的**（开发者演讲、CEDEC / GDC 都没有）。
触发条件、段数、开关这些有官网、报道、补丁说明可查；"是换网格还是换贴图"只有 mod 作者的资料和几条摘要。
表里每格后面的标记：【网页】打开读过，【摘要】只看到搜索摘要（Fandom 维基全部打不开，都算摘要），【推断】我的判断。来源网址在最后一节按编号列出。

| 游戏 | 什么时候破 | 几段 / 几个部位 | 看起来 | 露出什么，怎么做的 | 恢复 / 开关 |
|---|---|---|---|---|---|
| 灵魂能力 IV（2008） | 同一高度连续被打中（没挡住）；灵魂槽被打空时也破一处并短暂硬直，可接 Critical Finish【网页 G1 G2】 | 上 / 中 / 下 3 个区；破了的区之后多受伤害（玩家实测上、下约 116%，中约 106%）【网页 G1 G3】 | 永久的破损、撕烂的衣服【网页 G2】；碎裂音效【摘要 G4】 | 内衣和袜子留着【网页 G3】 | 整场保留，每回合都不恢复【摘要 G4】 |
| 灵魂能力 V（2012） | 重击 KO | 1 段：除了裤子、袜子、内衣全部掉【网页 G3】 | | 内衣 | |
| 灵魂能力 VI（2018） | Lethal Hit（每招有自己的条件：反击、打下蹲、打空招），奖励之一是"装备破坏"；超必杀 overkill KO【网页 G5 G6，摘要】 | 上 / 中 / 下 3 个区，按件【网页 G6】 | Lethal Hit 有慢镜头【网页 G7】；夹克破掉露出里面的衣服，重击剥掉全部装备（TGS 2018 演示）【网页 G8】；Nightmare 的头盔碎掉露出 Siegfried【网页 G9】 | 每个角色有设计好的内衣【网页 G10】，"大部分内衣不会破"【网页 G11】→ 内衣就是一个不会坏的装备槽【推断】 | 2.20 版（2020-08）自创角色可以按上 / 中 / 下分别关掉装备破坏【网页 G12 G6】 |
| 龙虎之拳 / 拳皇的"脱衣 KO"（1992 起） | 用必杀技、超必杀 KO 女性角色（King、Yuri）；KOF XIV 里投技型 Climax 超必杀也会【网页 G13 G14 G15，摘要】。闪乱神乐的制作人说自家的爆衣"深受龙虎之拳的脱衣 KO 影响"【网页 G16】 | 1 段，只在 KO | 上衣撕破 | 露内衣；KOF XIV 是"另一套模型"【摘要】 | 只在 KO，不用恢复 |
| 闪乱神乐 SHINOVI VERSUS（2013） | L+□ 打下装、L+△ 打上装，连段最后一击两边都打；内衣只能用秘传忍法的最后一击打掉【网页 G18】 | 上下各 3 段，3 段 = 全破 = 战斗不能【网页 G17】 | | 有内衣这一层 | 忍转身清除破损【摘要】；"命驾"：玩家自己扯掉衣服换攻击力【网页 G19】 |
| 闪乱神乐 EV（2015） | 每次秘传忍法上下都破，大约 3 下就全破【网页 G18】 | | 场景机关的特殊终结（撞章鱼壶、被樱花树挂住）【网页 G20】 | 全破时用"耀眼的光"挡住【网页 G20】 | |
| 闪乱神乐 Burst Re:Newal（2018） | 累积伤害破 2 段；第 2 段时被绝・秘传忍法打中 → 全破坏【网页 G21】 | 2 段 + 全破 | 每个角色专属的过场【网页 G21】 | 全裸 | 忍转身换忍装【网页 G21】 |
| 一骑当千（2007 PS2 / 2010 PSP） | 斗气槽满后的斗气必杀技，尤其拿来终结【网页 G22 G23 G24】 | 不明 | 撕衣，过场【网页 G24 G25】 | 不明 | |
| Queen's Blade Spiral Chaos（2009 PSP） | 攻击指定部位，每个部位的衣服各有 HP【网页 G26 G27】 | 头、胸、手、腰、腿 | 全部破坏 → 特殊演出，不管剩多少血直接 KO【网页 G27】 | | |
| Valkyrie Drive Bhikkhuni（2015 Vita） | 累积伤害撕裂，强攻击可整件破坏；解放满级的 Super Drive Break 整件破坏【网页 G28 G29】 | Drive 槽进化 4 次【网页 G30】 | Drive 演出 | 不明 | |
| DOA6（2019） | 破坏一击（Break Blow）可以划伤身体、撕破服装【网页 G31】；同一部位反复被打出现淤青、出血【网页 G32】 | 只有部分服装能破：**428 件里 44 件**带一个预先做好的 `<服装>a` 模型【本地 DOA6 数据】 | 实时变脏、出汗、擦伤、淤青；某些破坏一击有脸部特写【网页 G33】 | **命中时整件换成 a 版模型**【本地】：有的去掉外层（女天狗 `NYO_COS_030a` 少了外层布料和饰物），有的是撕破版（玛丽、穗香、蒂娜、绫音的 a 版多 500–950 个顶点：开了洞、补了皮肤）；DOA5 时代的 mod 资料：可破坏部件由物理文件指定"哪个物体掉下来"【网页 G35】 | 关掉暴力选项去掉血和脸部变形【网页 G36】；衣服变化本身关不掉【摘要】 |
| DOA5 / DOA5LR（2012 / 2015） | 落水、出汗、在地上滚【摘要】 | — | 湿身透视、变脏 | 不破衣。每件衣服有干、湿两张贴图加一张斑块遮罩，湿透的那张画着透出来的皮肤和内衣；每件衣服另有 4 个换贴图包，就是 4 款内衣【本地 DOA5LR 数据】 | 选项可关【摘要】 |
| 火影忍者 究极风暴 4（2016） | 受到一定伤害后"装备破坏"（约 40%）【网页 G37 G38，摘要】 | 1 段 | 装备、护甲掉落；防御降、攻击升 | | |
| 真人快打 9（2011）/ 不义联盟（2013） | 受到的伤害 | 按身体区域 | 伤口、破衣 | 伤害网格和正常网格放在同一个模型文件里，按区域切换；不义联盟美术的博客说：到阈值时把一块干净的网格换成破损的，服装、皮肤、肌肉、血按遮罩分层【摘要，博客已 404】。MK vs DC、MK9、MKX 都有伤口、破衣【网页 G39】 | |
| 铁拳 8（2024） | 倒地 | — | 衣服变脏【网页 G40】、淤青、帽子会掉【摘要】 | 不撕衣服 | |
| 超次元游戏 海王星 U（2014） | 服装耐久被打到 0 → Costume Break：防御降，暴击率和 EXE 槽升【网页 G41】 | 2 段（50%、100%）【摘要】 | 可跳过的短过场【网页 G42】 | 破烂的服装 | 有不会破的"硬"服装，没有开关【网页 G43】 |
| 尼尔：机械纪元（2017） | 按 L3+R3 自爆 → 2B 的裙子被炸飞【网页 G44】 | 1 段 | | 裙子下面本来就是紧身衣 | 快速旅行、读档恢复【网页 G45】 |
| Bullet Girls Phantasia（2018） | 头、胸、腰、两臂、两腿 7 个部位各有服装 HP，先扣服装再扣角色【网页 G46】 | 7 个部位 | 破坏演出 | | 演出可以关（初代）【网页 G47】 |
| （没有爆衣的）Onechanbara、Rumble Roses XX、神田川 Jet Girls、侍魂 2019 | 查过，没找到衣服破损；Onechanbara ORIGIN 是身上溅血当"槽"，侍魂只有溅血【网页 G48 G49 G50 G51】 | | | | |

ripper_tpose 之前的调研（`D_games_mods.md`）还有：秋叶原之旅（先削弱上中下的衣服再按键扒下），HS2 的"破损度"（着色器参数 `_AlphaEx` + 身体透明遮罩，本机读程序得到），恋活 / HS2 的衣服状态是换整个网格（`objTopDef` / `objTopHalf`），VaM 的 ClothingRipper（运行时沿切线删三角形 + 松开固定 + 碎片淡出）。

**对我们的启发**

1. **触发分三类**，可以组合：
   - KO 那一下（龙虎之拳 / 拳皇的脱衣 KO、灵魂能力 V）——最像我们这种 KOF 式格斗；
   - 特定招式（灵魂能力 VI 的 Lethal Hit、DOA6 的破坏一击、闪乱神乐的秘传忍法、一骑当千的斗气必杀技）——对应我们的技能和超必杀；
   - 累积伤害（灵魂能力 IV 按上中下区、Queen's Blade / Bullet Girls 按部位 HP、海王星 U 按服装耐久、火影约 40%）——对应"血量线"或"每个部位单独计伤害"。
2. **样子**：绝大多数是整件拿掉 / 换成破损版，配音效、慢镜头、镜头推近；擦边类游戏另加过场。没找到实时物理撕裂的例子；DOA6 的数据证实了它是"预做的破损模型整件替换"。
3. **露出**：要露出来，下面就得有东西。灵魂能力 VI、闪乱神乐都把内衣做成**单独的一层**（灵魂能力 VI 的基本不会破，闪乱神乐的只有秘传忍法的最后一击才打掉）。我们的战斗服恰恰没有这一层（衣服下面的皮肤删了），所以要"补身体"。
4. **恢复**：灵魂能力 IV 整场保留【摘要】，闪乱神乐靠变身恢复，尼尔读档恢复，脱衣 KO 只在最后一下所以不用恢复。
5. **开关**：灵魂能力 VI 可以按区关掉，Bullet Girls 可以关演出。我们是自用，至少留一个总开关方便看对比。

## 技术路线对比

"适合我们的素材"一栏的依据在下一节（【本地】核实）；其余各格后面的 T 编号是网页来源（都打开读过，除非标了【摘要】），列在最后一节。

| 路线 | 看起来是什么样 | 工作量 | 运行时开销 | 风险 | 适合我们的素材吗 |
|---|---|---|---|---|---|
| **A. 按件消失 + 快照碎片**：服装按现成的块分组，掉的那组材质 `_IGNOpacity` 归零；同一帧 `BakeMesh` 拍一份快照当碎片，自己积分飞出去、落地、抖动淡出 | 护甲、裙片整块飞出去 | 2.5 天（第 1、2 步） | `BakeMesh` 不管开没开 GPU 蒙皮都在 CPU 上算（T1）；论坛报的耗时从 4,101 顶点 50 µs 到 7,000 顶点 15 ms 都有（T2），要实测。只在爆开那一帧做一次，正好落在顿帧里。平时多几个子网格的绘制 | 低：不改骨架、不碰布料；只能按现成的块掉。材质要用实例，不用 MaterialPropertyBlock（它和 SRP Batcher 不兼容，T5） | **很适合**：服装天然分成 8 / 485 / 104 块；外层块下面皮肤完整 |
| **B. 预做的破损网格 / 破损贴图**：每段一套网格或一张带 alpha 洞的贴图，到时候换 | "破损形态"：衣服上出现撕口、缺口 | 每件衣服每段都要做（Blender 里用爆衣插件撕好、导回，或者画 alpha 洞） | 换网格 / 换贴图，几乎为零。换 `sharedMesh` 时各段的骨骼顺序必须一致（绑定矩阵按下标对应骨骼，T4） | 美术量大；洞下面没身体同样要补 | 战斗服没有破损版，要自己做；ROE 换装部件有现成的 `Broken` / `Hole` / `Open` 状态（见下一节） |
| **C. 形态键**：衣服缩进身体或被拉开 | 平滑地"拉开""褪下" | 每件做形态键 | Unity 6 在 GPU 上算，每个网格、每个生效的形态键一次 dispatch，默认会合批（T6） | 只能直线插值，像缩进去而不是撕开 | 一般，不推荐当主路线 |
| **D. 着色器破洞**：命中点换到绑定姿势空间，存成球；片元里按距离 + 噪声裁掉，边上压暗 / 毛边 / 烧边 | 打哪破哪，洞逐渐变大。L4D2 的伤口就是"在蒙皮前的姿势空间里用椭球裁像素 + 投影贴图做毛边"，顶点着色器 15 条指令、像素着色器 7 条（T11 T12） | 2–3 天（第 4 步） | 每像素十几个球的距离 + 一次噪声采样，小 | Unity 6 的蒙皮在计算着色器里先做完（T6），顶点着色器拿到的位置已经蒙皮过，所以绑定姿势坐标要预先存进一个 UV 通道【推断】；所有 pass 都要一样地裁（T7），不然会有"幽灵阴影"（T11）；洞是"消失"不是"掉下来"，要配一块反遮罩碎片；洞下面没身体会露空 → 要 E。URP 的贴花只能改颜色、法线、金属度 / 光滑度 / AO，挖不了洞（T14） | **适合**：着色器已有统一的裁剪挂点，服装材质本来就开了 alpha test（多一个裁剪条件不影响 Early-Z，T8）；ROE 自己的破丝袜就是 alpha 洞 + 毛边 |
| **E. 补身体**：家族底模做权重转移，放在衣服下面，按区显示 | 拿掉贴身衣服后是完整的身体 | 2 天（第 3 步） | 每个角色多 3.9 万三角形的 GPU 蒙皮，小 | g04 的手、脚形状和底模有 1–1.4 cm 差别，手套、鞋还在时要藏起来；接缝处共面会闪，用深度偏移 `Offset -1, -1`（T27） | **很适合**：套装皮肤就是从底模切的（≤1 mm），颜色差 ≈2/255。恋活 / HS2 正是"完整身体一直在 + 按衣服状态用遮罩藏起被盖住的部分"（T25）；CC4、UMA、模拟人生 4 则是直接隐藏 / 删掉被盖住的三角形（T22–T24）；VRChat 的 Modular Avatar 用形态键把被盖住的身体缩进去或删掉（T26）。用模板缓冲在屏幕上藏身体不行：挡在衣服前面的手臂也会被藏掉【推断，T28】 |
| **F. 运行时按命中切网格 + 物理**（切三角形、刚体、Unity Cloth / Magica / Obi） | 最"真"：任意位置撕开，布片飘落 | 1–2 周以上 | 命中那一帧切分、重建网格；布料每帧 CPU 解算（Obi 官方测的一个角色约 1.8 ms，2018 年的数据，T20） | 高：Unity Cloth 从 Unity 5 起不能撕，`maxDistance` 调大只是让顶点在一个球里活动、不会脱开（T18）；Magica Cloth 2 不能撕，MeshCloth 还要求网格可读（T19）；Obi 的可撕布料不支持蒙皮网格，作者建议"预先定好撕的位置，用缝合约束，到时删掉"（T20）；EzySlice 只切凸网格（T21），凸网格碰撞体最多 255 个三角形（T17）；我们的录像、检查是手动一步步推进的，这些插件不好控制 | 不适合第一版 |
| **G. 骨骼布料链松开**：裙骨链从骨盆上脱开，自由落下 | 裙片像布条一样滑落 | 0.5–1 天 | 现有解算 | 裙片靠腰的顶点还有骨盆权重，会被拉长 → 要配 A 的快照 | 可选的加分项 |
| **H. 离线烘好的爆衣动画**（Blender 爆衣插件 → 骨骼 / 顶点动画） | 最像真布料 | 每件每段都要烘 | 播放动画，小 | 起始姿势是烘的时候那一个，和格斗中的实际姿势对不上 | 只适合固定演出（比如超必杀 KO 的特写） |

另外两条现成的路：
- **运行时往贴图里画洞**（UV 空间画笔）：IRCSS/TexturePaint（MIT，顶点着色器把 UV 当位置输出，再做 UV 岛的接缝扩边，T13），Paint in 3D（商用，支持蒙皮网格，T13）。比 D 多一张每角色的遮罩贴图，好处是洞的形状可以任意画、能累积；坏处是 TexturePaint 用的是只有内置管线才有的相机命令缓冲，要改成 URP 的写法（T13）。D 的"球 + 噪声"对我们够用。
- **现成的溶解着色器**：Advanced Dissolve（$35，球、盒、平面等几何形状裁剪 + 发光边【摘要】，T31）；做法和 Cyanilux、Daniel Ilett 的教程一样（噪声和阈值比较，再偏一点的阈值做发光边，T9）。我们的着色器是手写的，抄算法比接插件省事。

## 我们的素材能提供什么（全部【本地】核实）

### 1. 角色在 Unity 里是怎么拼的

`RoeFighterBuilder` 把游戏的 HD 预制体原样实例化，材质按渲染器名从 meta 预制体抄过来（`Assets/RoeFighter/Editor/RoeFighterBuilder.cs`）。
拼好的 `Generated/<id>/<id>_fighter.prefab` 里：

| 角色 | 渲染器（SkinnedMeshRenderer） | 子网格 = 材质（着色器） | 三角形 |
|---|---|---|---|
| a08 | `pc_a08_hd_body1` | 0 `pc_a08_hd_skin`（ROE/Skin）+ 1 `pc_a08_hd_body1`（ROE/Character，开 alpha test） | 16,325 + 6,059 |
| a08 | `pc_a08_hd_body2` | 只有服装 `pc_a08_hd_body2` | 23,947 |
| a08 | `pc_a08_hd_head`、`pc_a08_hd_hair` | 脸、牙、眼、眉、泪；头发（第 3 个子网格是 body2 材质的发饰） | 17,232；18,911 |
| a08 | `wp_a08_l / r` | 剑 | |
| g04 | `pc_g04_hd_body` | 0 `pc_g_nk_face`（颈部，1,044）+ 1 `pc_g04_hd_skin` + 2 `pc_g04_hd_body`（服装，开 alpha test） | 1,044 + 13,224 + 26,112 |
| g04 | `pc_g03_hd_head`、`pc_g03_hd_hair` | 用的是 g03 的头和头发 | |
| g04 | `wp_g_04` | 扇子 | |

- **服装和露出的皮肤在同一个渲染器里，只是不同的子网格**（a08 的 body1、g04 的 body）。a08 另有一个纯服装的渲染器 body2。
- **没有"衣服下面的身体"这一层**，下一节量了缺多少。
- 网格资源都是 `m_IsReadable: 0`：打包后的 exe 里读不到三角形（`docs/magica-cloth-2.md` 已经踩过，改用 `BakeMesh` 的副本）。所以切分网格要在编辑器里做好、存成新资源。
- 着色器已经有现成的挂点：`ROE/Character` 的 `RoeClip()` / `RoeShadowClip()` 同时用于前向、阴影、深度、深度法线四个 pass（`Shaders/RoeCharacter.shader` 106–120 行，`RoePasses.hlsl`），
  还有游戏原有的屏幕门淡出 `_IGNOpacity`（`RoeCore.hlsl` 的 `RoeDitherClip`，交错梯度噪声，1 = 实，0 = 没）。加一个破损遮罩只要改这一处；碎片淡出直接用 `_IGNOpacity`。
- 格斗代码里有现成的触发点：`FightGame.CheckStrikes()` 知道打中的世界坐标 `p.Value`、伤害和方向；`SpecialHit()` 是技能伤害；KO 时已有 0.8 秒慢动作，超必杀有 0.25 秒慢动作；`Fighter.maxHp = 1000`。

### 2. 套装的皮肤不是完整身体：衣服挡住的地方被删掉了

拿家族的官方裸体底模（a 家族 `pc_a01_nk_bs`，g 家族 `pc_g01_nk_bs`，FBX 在 `D:\roe_exports\a01\pc_a01_nk_bs\` 和 `D:\roe_exports\g01\pc_g01_nk_bs\`）当"完整身体"，
和套装皮肤逐点比（表面到表面的距离，与三角化无关；脚本 `tools\research\clothes_burst\surf_test.py`）：

| | a08（对 a01 底模） | g04（对 g01 底模） |
|---|---|---|
| 底模身体面积（不含头） | 1.283 m² | 1.256 m² |
| 套装皮肤里有的 | 0.907 m²（71%） | 1.048 m²（83%） |
| **被删掉的（衣服下面）** | **0.376 m²** | **0.208 m²** |
| 套装皮肤离底模表面 | 中位 0.45 mm，90% 在 1 mm 内，99% 在 3 mm 内，没有超过 1 cm 的 | 中位 1.2 mm，90% 在 3.7 mm 内；超过 6 mm 的 106 个点几乎都在脚（高跟，最多 14 mm） |
| 两张皮肤贴图在同一位置的颜色差 | 平均 1.4–2.1 / 255，98.5% 的点 < 12 / 255 | 平均 1.3–1.9 / 255，99.4% < 12 / 255 |

结论：**套装的皮肤就是从家族底模上切下来的**（形状、贴图都一样），衣服盖住的部分删了。所以底模可以拿来补洞，接缝处颜色看不出差别。
图：`out\clothes_burst\nude_vs_suit_sheet.png`（红 = 套装里缺的皮肤），`out\clothes_burst\a08_sheet.png`、`out\clothes_burst\g04_sheet.png`（只画皮肤 / 皮肤 + 服装）。

每件衣服下面缺多少皮肤（`tools\research\clothes_burst\holes_per_piece.py`：缺的三角形归到 4 cm 内最近的那件衣服）：

| a08 的部件 | 下面缺的皮肤 | g04 的部件 | 下面缺的皮肤 |
|---|---|---|---|
| 两只护胫（body1，各 661 三角形） | 各 996 cm² | 两只手套（各 2,916） | 各 362 cm² |
| 胸甲 + 背带（body1，1,074） | 443 cm² | 两只鞋（各 709） | 各 345 cm² |
| 两只凉鞋（各 1,179） | 322 + 320 cm² | 上衣（2,724） | 172 cm²（乳头、背带下） |
| 两只护手（717 / 364） | 188 + 133 cm² | 内裤（858） | 151 cm² |
| 护裆（224） | 89 cm² | 主裙（前后长片 + 腰一圈，2,636） | 66 cm²（腰上一圈） |
| 长裙片、肩甲、胯甲、头冠、链子 | **0**（下面皮肤完整） | 后片、左飘带、装饰 | 4–14 cm² |

也就是说：**裙片、肩甲、胯甲、链子、头冠（a08）和裙片、后片、飘带（g04）直接拿掉就行，基本不会露洞**（g04 主裙腰上一圈缺 66 cm²）；护胫、护手、鞋、胸甲 / 上衣、护裆 / 内裤要先把身体补上。

### 3. 服装天然就分成一块一块

把服装子网格按"共用顶点"分成连通块（`tools\research\clothes_burst\outfit_pieces.py`，按位置焊接，再按权重最大的骨头命名）：

| 网格 | 连通块 | 主要的块 |
|---|---|---|
| a08 body1 服装 | 8 | 护胫 ×2（各 1,054 cm²）、胸甲（470）、凉鞋 ×2（449）、护手 ×2（263 / 219）、护裆（73） |
| a08 body2 | 485（其中链环等小块 33 个 < 5 cm²） | 长裙片 ×2（各 8,206 cm²，双面：79% 的三角形背后 4 mm 内有一个朝向相反的）、后腰垂片（1,652）、肩甲（714 + 324）、胯甲 ×2（714）、头冠几块（668 / 196 / 110…）、右前臂甲（433）、项圈（312）、链子 |
| g04 服装 | 104 | 主裙（5,244）、后片（2,185）、左飘带（1,612）、上衣（1,514）、前片（1,272）、鞋 ×2（456）、项圈（421）、手套 ×2（407）、内裤（168）、腰带（76）、发饰、流苏 |

块和块之间不相连，左右成对。**第一版完全不用切网格**：按块分组（按主骨名 + 左右）就能决定"哪一阶段掉哪几块"。

### 4. 骨架：底模比套装多骨头，不能直接按名字绑

套装 FBX 和底模 FBX 的蒙皮骨对比（`tools\research\clothes_burst\bones_cmp.py`）：

- a08：共用 104 根；底模身体用到、套装骨架里没有的有 53 根（`Butt_L/R`、`Tummy`、20 根脚趾骨、`Nipple_L/R`、4 根 `Point_breast_*`、18 根 `Muscle Strand*` 等；不算头发、脸、私处的骨头）。共用骨的绑定位置中位差 3.8 mm，最大 4.4 cm（膝盖辅助骨）。
- g04：共用只有 60 根，缺的有 59 根；除了上面那些，底模的 `Breast_L/R`、`ArmTwist_L/R`、`ThighTwist_*` 在 g04 里叫 `chest_L/R`、`UpArmTwist`，或者干脆没有。
- 所以"把底模挂到套装骨架上"要在编辑器里做一次**权重转移**：底模每个顶点取套装皮肤（和服装）上最近点的骨权重。两张皮肤表面重合，在 a08 上误差 1 mm 级。

### 5. ROE 自己有没有破损服装

- **战斗服装（a08、g04 这种 `chara_armor_pc_*`）没有任何破损版本。** 14,318 个游戏包的文件名里按 break / broken / damage / torn / tear / rip / crack / ruin / destroy / burst / shred / dmg / strip / undress / `_po_` 搜，命中的全是技能、关卡、界面名（`gameplay_skill_assist_destroy`、`broken_heart` 界面特效等）。玩法数据（`_work/gameplay`，982 个 JSON / Lua）里也没有。
- 战斗动作里的 `rip` 不是"撕衣服"：它是 0.5 秒的循环，`Bip001` 停在离地 13 cm、水平移开 1.4 m 的位置，正好是 `die` 最后一帧，即"倒地不起"（`tools\research\clothes_burst\rip_clip.py`）。
- 每个角色有自己的裸体相关包：`chara_bare_pc_a08_nk.ab`（5.6 MB）、`chara_bare_pc_g04_nk.ab`（6.1 MB）里是 H 场景的骨架和动作（`pc_a01_nk@eros15_*`），没有网格；`bare_blend_shape_pc_a08_nk.ab` / `_g04_nk.ab` 是那些场景的形态键数据，目标网格就是 `pc_a01_nk_body` / `pc_g01_nk_body`。也就是说，**游戏里 a08、g04 脱掉衣服用的就是家族底模**，和上面的比对一致。
- **换装套装（suit，不是战斗服）是分层的**：底模 + 头发 + 一堆单独的服装部件，运行时叠在底模上（`ripper_tpose/docs/roe-suit-assembly.md`）。这些部件里有**预先做好的"状态"**，已拼好的 67 套换装里有 26 套带（`D:\roe_exports\_suits\*\*\suit.json`）：
  - 敞开 / 拉下：`OpenSilkLingerie`、`Openshirt`、`SleepwearPull`、`UnderwearOpen`（a01）；`BrideBraOpen`、`BrideUnderwearOpen`、`OpenSecretaryRedBra`、`Sleepwearopenup / openbelow`（g01）；
  - 破洞：`StockingsBroken`（g01 兔女郎）、`SecretaryHoleStockings`（g01 秘书）、`WeddingTightsBroken`（k01，同一网格换材质）、`PantsHole`（k01 战斗装）。
  - `StockingsBroken` 的颜色贴图 alpha 里是大块的洞，洞边一圈拉丝状的毛边（`out\clothes_burst\stockingsbroken_alpha.png`）：**游戏自己做"破"的办法就是 alpha 贴图挖洞 + 画毛边**。
- 底模还配有内衣部件，可以当"最后一层"：a01 的 `Bra`、`Panties`、`Underwear`、`Swimbra`；g01 的 `Underwear`、`SportBra`、`Swimbra` 等（`D:\roe_exports\a01\`、`D:\roe_exports\g01\` 下的 `*_obj001`）。它们的骨也有一部分是套装骨架没有的（胸、臀、乳头点），同样要权重转移。

### 6. 建议的分组和阶段（按连通块的主骨自动归组，`tools\research\clothes_burst\stage_groups.py`；"下面缺皮肤" = 拿掉它会露出的洞）

a08（Inase）：

| 组 | 块数 | 三角形 | 面积 | 下面缺皮肤 | 建议阶段 |
|---|---|---|---|---|---|
| 长裙片（左右各一片，双面） | 2 | 5,644 | 16,412 cm² | 0 | 一 |
| 胯甲 | 11 | 3,362 | 1,726 cm² | 0 | 一 |
| 后腰垂片 | 1 | 1,050 | 1,652 cm² | 0 | 一 |
| 肩甲 + 肩上的链 | 6 | 1,886 | 1,194 cm² | 0 | 一 |
| 裙链 | 79 | 784 | 978 cm² | 0 | 一 |
| 右上臂的链（链环） | 166 | 664 | 1,409 cm² | 0 | 一 |
| 头冠 | 23 | 4,674 | 1,586 cm² | 0 | 不掉（或一） |
| 护手 / 前臂甲（含左臂链环） | 151 | 2,679 | 2,181 cm² | 597 cm² | 二（要补身体） |
| 护胫 | 2 | 1,322 | 2,108 cm² | 1,992 cm² | 二（要补身体） |
| 项圈 | 8 | 1,991 | 474 cm² | 0 | 二 |
| 胸甲 | 6 | 1,322 | 629 cm² | 469 cm² | 三（要补身体） |
| 护裆 | 1 | 224 | 73 cm² | 89 cm² | 三（要补身体） |
| 凉鞋 | 12 | 2,702 | 905 cm² | 674 cm² | 不掉 |

g04（Luffee）：

| 组 | 块数 | 三角形 | 面积 | 下面缺皮肤 | 建议阶段 |
|---|---|---|---|---|---|
| 主裙（前后两大片长裙 + 腰上一圈） | 1 | 2,636 | 5,244 cm² | 66 cm² | 一（腰上补一小圈） |
| 后片 + 下摆毛边 | 2 | 1,806 | 2,750 cm² | 14 cm² | 一 |
| 左飘带 | 2 | 1,382 | 2,032 cm² | 4 cm² | 一 |
| 前片 | 1 | 556 | 1,272 cm² | 0 | 一 |
| 裙饰 / 腰饰 / 流苏 | 58 | 3,342 | 340 cm² | 0 | 一 |
| 上衣 | 1 | 2,724 | 1,514 cm² | 172 cm²（乳头、背带下） | 二（要补身体） |
| 项圈 | 4 | 1,790 | 452 cm² | 12 cm² | 二 |
| 腰带 | 7 | 1,226 | 106 cm² | 50 cm² | 二 |
| 手套 | 2 | 5,832 | 814 cm² | 724 cm²（整只手） | 二（要补身体） |
| 内裤 | 2 | 1,104 | 195 cm² | 151 cm² | 三（要补身体） |
| 头饰 | 18 | 2,008 | 176 cm² | 0 | 不掉 |
| 鞋 | 4 | 1,518 | 983 cm² | 711 cm² | 不掉 |

- "一"段两人都**不需要补身体**（g04 主裙腰上 66 cm² 那一圈除外，可以等到"二"段一起补，或者让主裙在"二"段才掉）。
- 鞋建议一直留着：g04 的脚和 g01 底模差 1–1.4 cm（高跟角度不同），补出来的脚容易从鞋里穿出来。a08 的脚和底模一致，凉鞋能脱，脱不脱看你。
- 分组规则是按主骨名写的，第一次跑可能有零星的块归错（比如 a08 手指上的 10 个小块、耳饰，上表没列）；做成 JSON 表，跑完出一张"每组一种颜色"的检查图再改。现在的分组图：`out\clothes_burst\a08_groups.png`、`out\clothes_burst\g04_groups.png`（灰 = 皮肤）。

静态预览（T 姿势，平涂，不是游戏画面）：`out\clothes_burst\a08_stages.png`、`out\clothes_burst\g04_stages.png`。灰色是套装自己的皮肤，橙粉色是从底模补上的身体，蓝 / 绿 / 橙是还留着的第一 / 二 / 三段衣服，紫色是不掉的（头冠、凉鞋、头饰、鞋）。

### 7. 文件位置

| 内容 | 位置 |
|---|---|
| Unity 里的网格 / 材质 / 贴图 | `E:\code\othercode\roe_fighter_unity\Assets\ROE\a08\chara_armor_pc_a08_hd_ld_hd\`（`pc_a08_hd_body1/body2/head/hair.asset`），`...\g04\chara_armor_pc_g04_hd_ld_hd\pc_g04_hd_body.asset`；材质在 `chara_mat_armor_*`，贴图在 `chara_tex_armor_*` |
| 拼好的格斗预制体 | `Assets\RoeFighter\Generated\a08\a08_fighter.prefab`、`...\g04\g04_fighter.prefab` |
| 底模 FBX | `D:\roe_exports\a01\pc_a01_nk_bs\FBX_GameObjects\pc_a01_nk_bs\pc_a01_nk_bs.fbx`、`D:\roe_exports\g01\pc_g01_nk_bs\...\pc_g01_nk_bs.fbx` |
| 底模贴图 | `D:\roe_exports\a01\_textures\pc_a01_nk_body_rgbx_*.png`、`D:\roe_exports\g01\_textures\pc_g01_nk_body_rgbx_*.png` |
| 底模成品（带材质） | `E:\game_export\RiseOfEros\Inase\{blend,pmx,xps}\pc_a01_nk_bs\`、`...\Luf\{blend,pmx,xps}\pc_g01_nk_bs\` |
| 内衣 / 状态部件 FBX | `D:\roe_exports\a01\<部件>_obj001\`、`D:\roe_exports\g01\<部件>_obj001\`；拼好的换装套装 `D:\roe_exports\_suits\<id>\<suit>\` |
| 游戏原包 | `D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles\`（`chara_bare_pc_a08_nk.ab` 等） |
| 这次的分析脚本和图 | `tools\research\clothes_burst\`（脚本）、`out\clothes_burst\`（图，不进仓库） |

## 建议的实施步骤

工作量按"我在这个工程里连续做"估，含检查图和对比视频；第一次接触的部分（底模导入、着色器）可能多半天到一天。

| 步 | 做什么 | 工作量 | 完成的标志 |
|---|---|---|---|
| 0 | 你定规则（下一节）；我把分组表写成 JSON，出每组一种颜色的检查图 | 0.5 天 | 检查图里没有归错的块 |
| 1 | **编辑器：拆分组**（`RoeBurstBuilder`）。读服装网格（编辑器里可读），按分组表把服装子网格拆成"每组一个子网格"，存成新网格资源放 `Generated/<id>/burst/`；每组一个材质实例（同一套贴图）。拼角色时换上这份网格 | 1 天 | 爆衣前和原来逐像素一样（同一帧渲染对比） |
| 2 | **运行时：按件掉落**（`RoeClothesBurst`）。触发条件挂在 `Fighter.TakeHit` / `FightGame.SpecialHit`；到某一段时：该段各组的材质 `_IGNOpacity = 0`（四个 pass 一起消失，阴影也没了）；同一帧对这个渲染器 `BakeMesh`，只留这几组的子网格，生成碎片物体；碎片在 `FightGame.Step` 里自己积分：初速度 = 打击方向 × 2–4 m/s + 径向 + 随机角速度，重力，落地反弹 + 摩擦，0.4–1 秒后用 `_IGNOpacity` 抖动淡出；加碎布 / 金属火花粒子（游戏自己的特效）、音效、多 2–4 帧顿帧。随机数用比赛的种子，回放和录像一致 | 1.5 天 | 电脑对电脑录一场：两人都按阶段掉，`RoeFightProbe` 式的逐段截图 |
| 3 | **补身体**。导入家族底模（`chara_bare_pc_a01_nk` / `g01_nk` 走现有的 AssetRipper 流程，或用 `D:\roe_exports` 的 FBX + 贴图）；编辑器里做权重转移（底模每个顶点取套装皮肤 ∪ 服装上最近点的骨权重）；按"被哪组衣服盖着"把底模分区（常显区 / 每组一个区）；ROE/Skin 加 `_IGNOpacity`（或分区用单独渲染器开关）。某组掉了，就显示它下面那一区（恋活 / HS2 就是"完整身体 + 按衣服状态的遮罩"，T25）。接缝两种处理：只补缺的三角形 + 深度偏移（`Offset -1, -1`，T27）盖住边缘；或者第一次补身体时把整张套装皮肤换成底模（两者表面差 ≤1 mm、颜色差 ≈2/255，换的那一帧看不出来） | 2 天 | 第二、三段拿掉护胫 / 胸甲 / 上衣 / 手套后，正反两面没有洞（量法：把皮肤和底模一起烘出来，查背景透过身体的像素数 = 0） |
| 4 | **着色器破洞**（可选，升级）。网格多存一份"绑定姿势坐标"（拆分组时顺手写进 UV2）；命中时把 `CheckStrikes` 的命中点换到绑定姿势空间（用附近权重最大的那根骨：（骨骼当前矩阵 × 绑定矩阵）的逆 × 命中点，见下面的草图），存进每个角色最多 16 个"洞"（球心 + 半径 + 开始时间）；`RoeClip` 里按距离 + 噪声贴图裁掉，边上一圈压暗 / 毛边（照 Blender 爆衣插件的 `look.py`），技能命中可以换成发光烧边。同时生成一块反遮罩的碎片（同一份快照，只画被裁掉的那块）飞出去 | 2–3 天 | 打哪破哪；洞下面已经有身体（第 3 步） |
| 5 | 可选：裙片像布条一样掉（`RoeBoneCloth` 加"松开链根"，裙片快照跟着链骨落下）；内衣层（a01 的 `Bra` / `Panties`，g01 的 `Underwear` / `SportBra`，同样做权重转移）当"最后一段"前的一层 | 各 0.5–1 天 | |

**两个关键做法的草图**【推断，没写代码】

分组表（每个角色一份 JSON，第 0 步出，第 1、2 步读）：

```json
{
  "id": "a08",
  "groups": [
    { "name": "skirt",   "renderer": "pc_a08_hd_body2", "bones": ["^Skirt_[LR]_(0[1-9]|1\\d)$"], "stage": 1, "style": "fly" },
    { "name": "greaves", "renderer": "pc_a08_hd_body1", "bones": ["CalfTwist"],                    "stage": 2, "style": "fly", "needsBody": true },
    { "name": "bra",     "renderer": "pc_a08_hd_body1", "bones": ["^Bip001 Spine1$", "Breast"],     "stage": 3, "style": "fly", "needsBody": true }
  ],
  "stages": [ { "hpBelow": 0.66 }, { "hpBelow": 0.33 }, { "ko": true } ]
}
```

命中点换到绑定姿势空间（第 4 步）：网格顶点本来就是绑定姿势下的坐标，拆分组时把它原样再存一份到 UV2（Unity 蒙皮只改位置、法线、切线，UV 原样传给着色器）；
命中时取命中点附近权重最大的骨 b，`rest = (bones[b].localToWorldMatrix * mesh.bindposes[b]).inverse.MultiplyPoint(hitWorld)`，
着色器里 `clip(length(uv2 - rest) / radius + 噪声 - 1)`。这和 L4D2 用"蒙皮前的位置"在椭球里裁像素是同一个思路（T11）；L4D2 最后同时只开 2 个伤口，硬边不好看，改用投影贴图做毛边（T11），我们可以用噪声贴图或距离场贴图（T10）。

另一种做法：不拆子网格，在顶点上存组号（UV3），着色器按"已掉的组"的位掩码裁掉，碎片从快照里按组号挑三角形。拆子网格不用改着色器、多几个绘制；存组号只有一个绘制、要改着色器。两种都行，第一版用拆子网格。

**每步都要注意的坑**

- 打包后网格不可读：拆分、权重转移都在编辑器里做（编辑器里读不可读的网格可以用 `MeshUtility.AcquireReadOnlyMeshData`，T3；工程里现有的编辑器脚本也一直在读），存成新资源；运行时只用 `BakeMesh` 的结果（脚本新建的网格默认可读，T3）。
- `BakeMesh` 一定在 CPU 上蒙皮（T1），论坛报的耗时差两个数量级（T2）：第 2 步先用 `RoeFightProbe` 式的探针量一下 a08 body2（17,005 顶点）和 g04 body（25,066 顶点）各要多久。太慢就把每组拆成单独的渲染器，只烘那一组。
- 不要拿 `GetVertexBuffer` 做碎片：有"不可读网格 + Raw 目标，打包后网格消失"的报告（6000.0.42–6000.3，T15）；VFX Graph 的 Sample Skinned Mesh 也要求网格可读（T16），想沿衣服表面喷碎布粒子，就用拆好的新网格或快照。
- ROE/Skin 原来没有 discard，加 `_IGNOpacity` 会影响皮肤的 Early-Z（T8）：用 `shader_feature` 关键字，只有补身体的材质带裁剪；或者干脆用渲染器开关来藏。
- 裙子的布料：拆分后裙骨链照常模拟；某组裙片掉了以后，把它的链从 `RoeBoneCloth` 里停掉（省时间，也避免被拉长的权重乱动）。
  `RoeBodySurface` 取腿上的点时把护胫、鞋也算成"皮肤"（README 布料一节），护胫掉了以后这些点要换成底模的腿。
- 录像和检查是在批处理里手动一步步推进的：碎片、粒子都要能手动推进（碎片自己积分）。
  不要用 PhysX 刚体 / Magica / Obi 来做碎片：Magica 没有手动推进的接口（`docs/magica-cloth-2.md` 第 8 节）；PhysX 要改成脚本模式自己调 `Physics.Simulate`，每次传固定步长才是确定的（T29）；粒子用 `ParticleSystem.Simulate` 手动推进（T30）。
- 两个角色同时可能有十几块碎片在飞，每块几百到几千个三角形，不用合批也不会有压力。
- 做成独立组件，只接收几个事件："打中了"（位置、方向、伤害、是不是技能 / 超必杀）、"KO"、"回合 / 比赛开始"。以后换 UFE，接上同样的事件就行。

## 要你定的事

1. **做到哪一步**：只掉外层（裙片、肩甲、胯甲、链子；第 1、2 步就够），还是要拿掉贴身的护甲 / 上衣（要第 3 步补身体），还是全掉（胸甲、护裆、内裤）。
2. **什么时候掉**：
   - 按血量（比如剩 66%、33% 各掉一段，跨过线的那一下打中时掉）；
   - 只有技能 / 超必杀打中才掉（像闪乱神乐的"秘传忍法"、DOA6 的破坏一击）；
   - KO 那一下：要么掉最后一段，要么只在 KO 时一次掉（龙虎之拳 / 拳皇的"脱衣 KO"，最像我们这种格斗，也最简单）；KO 已有 0.8 秒慢动作；
   - 每回合恢复，还是整场比赛保留（灵魂能力 IV 是整场保留【摘要】）。
3. **掉的方式**：护甲飞出去、布料撕开 / 烧掉，还是两种都要；技能（扇子的风、剑气）要不要烧边发光。
4. **掉哪边**：按命中的位置（打上身掉上身、打左边掉左边），还是按固定顺序。
5. **要不要内衣层**：用 ROE 自己的内衣部件，在"全掉"之前多一段。
6. **要不要换一套"分层"的衣服**：ROE 的换装套装本来就是"底模 + 部件"，还带敞开 / 破洞状态（比如 g01 秘书装的 `OpenSecretaryRedBra`、`SecretaryHoleStockings`），换成它们爆衣会更简单，但那就不是现在这两套战斗服了。

## 来源

### 技术（"技术路线对比"等处的 T 编号；2026-10-03 查，都打开读过，除非注明）

- T1 Unity：`SkinnedMeshRenderer.BakeMesh`（6000.4；"蒙皮一定在 CPU 上做，不管 GPU 蒙皮开没开"）：https://docs.unity3d.com/6000.4/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html
- T2 Unity 论坛：BakeMesh 耗时（两份报告相差很大）https://discussions.unity.com/t/skinnedmeshrenderer-bakemesh-slow-performance/776033 ；Unity 员工说 BakeMesh 在 CPU 上算、建议用 `GetVertexBuffer`：https://discussions.unity.com/t/feature-request-skinnedmeshrenderer-bakemeshasync/1718551
- T3 Unity：`Mesh.isReadable`（"脚本新建的网格默认可读"）https://docs.unity3d.com/ScriptReference/Mesh-isReadable.html ；`Mesh.AcquireReadOnlyMeshData`（不可读时抛异常，编辑器里可用 `MeshUtility.AcquireReadOnlyMeshData` 跳过）https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Mesh.AcquireReadOnlyMeshData.html ；Unity C# 源码 `Mesh.cs`（不可读时 `SetTriangles` / `SetIndices` 报错）https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Graphics/Mesh.cs
- T4 Unity：`SkinnedMeshRenderer.sharedMesh` https://docs.unity3d.com/ScriptReference/SkinnedMeshRenderer-sharedMesh.html ；`Mesh.bindposes` https://docs.unity3d.com/ScriptReference/Mesh-bindposes.html ；`Renderer.enabled` https://docs.unity3d.com/ScriptReference/Renderer-enabled.html ；`Renderer.sharedMaterials` https://docs.unity3d.com/ScriptReference/Renderer-sharedMaterials.html
- T5 Unity：`MaterialPropertyBlock`（与 SRP Batcher 不兼容）https://docs.unity3d.com/ScriptReference/MaterialPropertyBlock.html ；https://docs.unity3d.com/6000.0/Documentation/Manual/SRPBatcher-Incompatible.html ；`Renderer.material`（自动实例化，要自己销毁）https://docs.unity3d.com/ScriptReference/Renderer-material.html
- T6 Unity 6000.4：`PlayerSettings.meshDeformation`（网格变形在 GPU 计算着色器里做）https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PlayerSettings-meshDeformation.html ；形态键开销的社区实测：https://gist.github.com/d4rkc0d3r/f77c1e96d4aeefd0d1eaf13fb096a2de
- T7 Unity：URP 深度 pass（各 pass 要裁出同样的片元）https://docs.unity3d.com/6000.1/Documentation/Manual/urp/writing-shaders-urp-depth-only.html ；URP 自带 `Lit.shader` 在所有 pass 里编 `_ALPHATEST_ON`：https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader
- T8 MJP，《To Early-Z, or Not To Early-Z》（含 discard 的着色器对 Early-Z 的影响）：https://therealmjp.github.io/posts/to-earlyz-or-not-to-earlyz/
- T9 溶解教程：Cyanilux https://www.cyanilux.com/tutorials/dissolve-shader-breakdown/ ；Daniel Ilett（URP）https://danielilett.com/2020-04-15-tut5-4-urp-dissolve/
- T10 Chris Green（Valve），SIGGRAPH 2007，距离场 alpha 测试（描边、发光由两个阈值控制）：https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf
- T11 Alex Vlachos（Valve），《Rendering Wounds in Left 4 Dead 2》，GDC 2010：https://steamcdn-a.akamaihd.net/apps/valve/2010/gdc2010_vlachos_l4d2wounds.pdf
- T12 Bronwen Grimes（Valve），《Shading a Bigger, Better Sequel: Techniques in Left 4 Dead 2》，GDC 2010（第 53–69 页是伤口）：https://cdn.cloudflare.steamstatic.com/apps/valve/2010/GDC10_ShaderTechniquesL4D2.pdf
- T13 IRCSS/TexturePaint（MIT）：https://github.com/IRCSS/TexturePaint ；Paint in 3D 文档：https://carloswilkes.com/Documentation/PaintIn3D
- T14 Unity：URP 贴花 https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-feature-decal-reference.html ，https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-feature-decal.html
- T15 Unity：`SkinnedMeshRenderer.GetVertexBuffer` https://docs.unity3d.com/ScriptReference/SkinnedMeshRenderer.GetVertexBuffer.html ；打包后网格消失的论坛报告（不可读网格 + Raw 目标，6000.0.42–6000.3）：https://discussions.unity.com/t/mesh-getvertexbuffer-cause-mesh-disappear-in-build/1702783
- T16 VFX Graph：Sample Skinned Mesh（网格不可读时返回 0）：https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.0/manual/Operator-SampleSkinnedMesh.html
- T17 Unity：Mesh Collider（凸网格最多 255 个三角形）：https://docs.unity3d.com/Manual/class-MeshCollider.html
- T18 Unity：Cloth https://docs.unity3d.com/Manual/class-Cloth.html ；Unity 5 升级指南（不再支持撕裂）https://docs.unity3d.com/2017.4/Documentation/Manual/UpgradeGuide5-Physics.html ；`Cloth.bindings.cs` https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/Cloth/Cloth.bindings.cs
- T19 Magica Cloth 2：运行时构建 https://magicasoft.jp/en/mc2_runtime_build/ ，MeshCloth 入门（要求网格可读）https://magicasoft.jp/en/mc2_meshclothstartguide/ ，性能 https://magicasoft.jp/en/mc2_performance/ ，概述 https://magicasoft.jp/en/mc2_about/ 。"不能撕"是作者 2022-04-20 在初代 MagicaCloth 讨论串里说的（ripper_tpose 的 `B_realtime_code.md` 已核实）；MC2 文档没提撕裂
- T20 Obi Cloth：撕裂 https://obi.virtualmethodstudio.com/manual/7.0/clothtearing.html ；作者建议用缝合约束 https://obi.virtualmethodstudio.com/forum/thread-3838.html ；性能 https://obi.virtualmethodstudio.com/performance.html
- T21 EzySlice（MIT）：https://github.com/DavidArayan/ezy-slice
- T22 模拟人生 4：Sims 4 Studio 教程（删掉被衣服挡住的身体）https://sims4studio.com/thread/17054/tutorial-full-body-outifts ；MTS https://db.modthesims.info/showthread.php?t=552540
- T23 Character Creator 4：隐藏内层网格 https://manual.reallusion.com/Character-Creator-4/Content/ENU/4.0/08_Cloth/Hiding_Inner_Meshes.htm ，自动隐藏 https://manual.reallusion.com/Character-Creator-4/Content/ENU/4.0/08_Cloth/Automatically_Hiding_Inner_Meshes.htm
- T24 UMA `MeshHideAsset`（每个三角形一位的隐藏表）：https://github.com/umasteeringgroup/UMA/blob/master/UMAProject/Assets/UMA/Core/StandardAssets/UMA/Scripts/MeshHideAsset.cs
- T25 恋活 / HS2 的身体遮罩（"用来藏起身体的一部分，免得和衣服穿模"；红 = 上衣穿好时可见、绿 = 半脱时可见、黄 = 都可见、黑 = 两种状态都藏、衣服全脱才显示）：https://github.com/ManlyMarco/Illusion-Overlay-Mods/blob/master/Guide/How%20to%20edit%20body%20masks.md
- T26 Modular Avatar（VRChat）Shape Changer：https://modular-avatar.nadena.dev/docs/reference/reaction/shape-changer
- T27 Unity：深度偏移 https://docs.unity3d.com/6000.3/Documentation/Manual/writing-shader-set-depth-bias.html ，https://docs.unity3d.com/Manual/SL-Offset.html
- T28 Unity：模板 https://docs.unity3d.com/Manual/SL-Stencil.html ；URP 可自用模板缓冲的第 0–3 位：https://docs.unity3d.com/6000.4/Documentation/Manual/urp/urp-universal-renderer.html
- T29 Unity：`Physics.Simulate`（脚本模式下手动推进；要确定性就每次传固定步长）：https://docs.unity3d.com/ScriptReference/Physics.Simulate.html
- T30 Unity：`ParticleSystem.Simulate`：https://docs.unity3d.com/ScriptReference/ParticleSystem.Simulate.html
- T31 Advanced Dissolve（Amazing Assets，$35；功能列表只看到摘要）：https://assetstore.unity.com/packages/vfx/shaders/advanced-dissolve-111598
- 之前的调研（Obi、PhysX、FleX、VaM ClothingRipper、HS2 `_AlphaEx` 等）见 `ripper_tpose/research_notes/爆衣效果调研/B_realtime_code.md`、`D_games_mods.md`。

### 游戏（"别的游戏怎么做"表里的编号；2026-10-03 查，除 G4 外都打开读过）

- G1 8wayrun，灵魂能力 IV 装备破坏怎么触发：https://8wayrun.com/threads/how-does-armor-destruction-work.166/
- G2 Wikipedia：Soulcalibur IV：https://en.wikipedia.org/wiki/Soulcalibur_IV
- G3 8wayrun，历代系统：https://8wayrun.com/threads/game-mechanics-past-present-future.16963/
- G4 Soulcalibur Wiki：Equipment Destruction（只有摘要，Fandom 打不开）：https://soulcalibur.fandom.com/wiki/Equipment_Destruction
- G5 The Outer Haven，SCVI 系统：https://www.theouterhaven.net/2018/09/soul-calibur-vi-gameplay-mechanics/
- G6 note（saratto），SCVI 部位破坏：https://note.com/saratto/n/n4d043e5a637c
- G7 电击 Online，Lethal Hit 慢镜头：https://dengekionline.com/elem/000/001/822/1822888/
- G8 GAME Watch，TGS 2018 舞台演示：https://game.watch.impress.co.jp/docs/news/1144419.html
- G9 PlayStation Blog 2018-02-28：https://blog.playstation.com/2018/02/28/armor-breaks-one-liners-and-facial-fuzz-12-reasons-why-we-already-love-soulcalibur-vi/
- G10 goziline，SCVI 角色内衣：https://goziline.com/archives/28335
- G11 Steam 社区，SCVI 内衣不会破：https://steamcommunity.com/app/544750/discussions/0/1643170269574976092/
- G12 SCVI 2.20 / 2.21 补丁说明：https://en.bandainamcoent.eu/soulcalibur/news/soulcalibur-vi-update-ver-220-and-221-patch-notes-playstation4rxbox-onesteamr ；日文更新履历：https://sc6.soularchive.jp/update/history.php
- G13 Wikipedia（日）：キング（龍虎の拳）：https://ja.wikipedia.org/wiki/%E3%82%AD%E3%83%B3%E3%82%B0_(%E9%BE%8D%E8%99%8E%E3%81%AE%E6%8B%B3)
- G14 电击 Online，KOF '94：https://dengekionline.com/articles/49945/
- G15 格ゲーブログ，KOF XIV：https://kakugeblog.blog.jp/archives/58108869.html
- G16 4Gamer，闪乱神乐制作人访谈（脱衣 KO 的影响）：https://www.4gamer.net/games/389/G038902/20180226062/
- G17 Famitsu，SHINOVI VERSUS：https://www.famitsu.com/news/201212/13025840.html
- G18 Operation Rainfall，EV 试玩（续）：https://operationrainfall.com/2015/06/20/hands-senran-kagura-estival-versus-follow/
- G19 Operation Rainfall，EV 试玩（命驾）：https://operationrainfall.com/2015/06/18/hands-senran-kagura-estival-versus/
- G20 闪乱神乐 EV 官网系统页：https://senrankagura.marv.jp/series/kaguraEV/system/
- G21 闪乱神乐 Burst Re:Newal 官网：https://senrankagura.marv.jp/series/burst_re/sp/system/action.html ，https://senrankagura.marv.jp/series/burst_re/sp/system/burst.html
- G22 一骑当千 Shining Dragon 官网系统页：https://www.marv.jp/special/game/ps2/ikkitosen/system/system03.html
- G23 一骑当千 Eloquent Fist 官网系统页：https://www.marv.jp/special/game/psp/ikkitosen/system/system3.html
- G24 GAME Watch：Xross Impact https://game.watch.impress.co.jp/docs/news/323990.html ，Shining Dragon https://game.watch.impress.co.jp/docs/20070521/ikki.htm
- G25 HonestGamers，Shining Dragon 评测：http://www.honestgamers.com/6287/playstation-2/ikki-tousen-shining-dragon/review.html
- G26 4Gamer，Queen's Blade Spiral Chaos：https://www.4gamer.net/games/089/G008967/20090710023/
- G27 gamelifeofme，Queen's Blade：https://gamelifeofme.com/queensblade/
- G28 Valkyrie Drive Bhikkhuni 官网系统页：https://valkyriedrive.marv.jp/bhikkhuni/system/
- G29 电击 Online，Valkyrie Drive：https://dengekionline.com/elem/000/001/168/1168252/
- G30 Siliconera，Valkyrie Drive 的服装破坏：https://www.siliconera.com/valkyrie-drive-bhikkhuni-details-more-of-its-battle-and-costume-break-systems/
- G31 Famitsu，DOA6 新堀访谈：https://www.famitsu.com/news/201806/09158729.html
- G32 4Gamer，DOA6 淤青和出血：https://www.4gamer.net/games/422/G042216/20180615175/
- G33 4Gamer，DOA6 实时脏污、出汗：https://www.4gamer.net/games/422/G042216/20190219091/
- G34 格ゲーブログ，DOA6 撕口"像画上去的"：https://kakugeblog.blog.jp/archives/76647164.html
- G35 Free Step Dodge，服装 mod（.PHYD 指定掉落物体）：https://www.freestepdodge.com/threads/costume-mods.6327/page-33
- G36 Famitsu，DOA6 暴力选项：https://www.famitsu.com/news/201809/21164508.html ；Wikipedia：https://en.wikipedia.org/wiki/Dead_or_Alive_6
- G37 火影忍者 究极风暴 4 官网系统页：https://naruto-game.bngames.net/sp/system/system02.html
- G38 电击 Online，究极风暴 4：https://dengekionline.com/elem/000/001/098/1098220/
- G39 TRMK 论坛，MK 战斗伤害：https://www.trmk.org/forums/threads/mortal-kombat-x-battle-damage.31813/ ；Wikipedia：MK vs DC Universe：https://en.wikipedia.org/wiki/Mortal_Kombat_vs._DC_Universe
- G40 Fighters Generation，铁拳 8：https://www.fightersgeneration.com/news1/tekken8-sep22-2.htm
- G41 4Gamer，海王星 U：https://www.4gamer.net/games/258/G025851/20140808150/
- G42 GamingTrend，海王星 U 评测：https://gamingtrend.com/reviews/a-breath-of-fresh-air-for-nep-hyperdimension-neptunia-u-action-unleashed-review/
- G43 Steam 社区，海王星 U：https://steamcommunity.com/app/387340/discussions/0/385428943464666701/
- G44 RPG Site，尼尔自爆：https://www.rpgsite.net/news/5383-nier-automatas-self-destruct-command-doesnt-do-what-you-think-it-would
- G45 神ゲー攻略，尼尔：https://kamigame.jp/nierautomata/page/275188572850492042.html
- G46 ameblo，Bullet Girls Phantasia：https://ameblo.jp/zeromeba/entry-12643087240.html
- G47 Wikipedia（日）：バレットガールズ：https://ja.wikipedia.org/wiki/%E3%83%90%E3%83%AC%E3%83%83%E3%83%88%E3%82%AC%E3%83%BC%E3%83%AB%E3%82%BA
- G48 inside-games，Onechanbara ORIGIN：https://www.inside-games.jp/article/2019/10/10/125132.html
- G49 Wikipedia：Rumble Roses XX：https://en.wikipedia.org/wiki/Rumble_Roses_XX
- G50 Steam 社区，神田川 Jet Girls：https://steamcommunity.com/app/1233800/discussions/0/3935537639871135660/
- G51 SNK，侍魂 2019 系统页：https://www.snk-corp.co.jp/official/samuraispirits/gamesystem/
- 之前的调研（秋叶原之旅、HS2、恋活、VaM、SF6 的 CEDEC 分享等）的出处见 `ripper_tpose/research_notes/爆衣效果调研/D_games_mods.md`。
- DOA5LR / DOA6 的衣服和布料（本机数据：DOA6 的 `<服装>a.g1m`、DOA5LR 的干 / 湿贴图和换贴图包）：ripper_tpose `docs/doa-clothing-and-cloth.md`。

### 本地（【本地】）

- Unity 工程：`E:\code\othercode\roe_fighter_unity\README.md`、`docs\magica-cloth-2.md`；
  `Assets\RoeFighter\Editor\RoeFighterBuilder.cs`、`Editor\RoeFightScene.cs`；
  `Assets\RoeFighter\Runtime\Fight\FightGame.cs`（`CheckStrikes`、`SpecialHit`、KO 慢动作、`seed`）、`Fighter.cs`（`maxHp`、`TakeHit`）、`FighterRig.cs`（`ShowWeapons`、`Anchor`）；
  `Runtime\RoeBoneCloth.cs`、`RoeBodySurface.cs`、`RoeSkirtRig.cs`；
  `Assets\RoeFighter\Shaders\RoeCharacter.shader`（`RoeClip` / `RoeShadowClip` / `_IGNOpacity`）、`RoeCore.hlsl`（`RoeDitherClip`、`RoeCutoutAlpha`）、`RoePasses.hlsl`、`RoeSkin.shader`；
  `Assets\ROE\a08\...`、`Assets\ROE\g04\...` 的网格（`.asset`）、预制体、材质；`Assets\RoeFighter\Generated\a08\a08_fighter.prefab`、`g04\g04_fighter.prefab`；
  `_work\gameplay\`（玩法数据，982 个文件）。
- ROE 数据：`D:\roe_exports\a01\`、`g01\`、`a08\`、`g04\`、`_suits\`、`nude_materials\nude_models_manifest.json`；`E:\game_export\RiseOfEros\README.md`；
  游戏包目录 `D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles\`（只列文件名，用 UnityPy 只读看了 `chara_bare_pc_a08_nk`、`bare_blend_shape_pc_a08_nk / g04_nk`、`chara_naked_pc_a08`）。
- ripper_tpose：`scripts\riseoferos\README.md`（§4 裸模）、`docs\roe-hq-materials.md`、`docs\roe-suit-assembly.md`（换装 = 底模 + 部件、"替代态"规则）、
  `docs\clothes-burst-survey.md`、`research_notes\爆衣效果调研\B_realtime_code.md`、`D_games_mods.md`、`docs\clothes-burst-guide.md` §7（毛边 / 溶解的做法）、`scripts\blender_addons\clothes_burst\`。
- 这次的脚本（都只读，在 `tools\research\clothes_burst\`）：`unity_mesh.py`（解 Unity 网格 YAML）、`fbx_bin.py`（读二进制 FBX）、`skin_coverage.py`、`nude_vs_suit.py`、`surf_test.py`、`far_verts.py`、
  `bones_cmp.py` / `bones_list.py`、`outfit_pieces.py`、`holes_per_piece.py`、`uncovered.py`、`stage_groups.py`、`group_picture.py`、`stage_preview.py`、`skin_color_cmp.py`、`comp_check.py`、`rip_clip.py`、`list_bundles.py`、`peek_text.py`、`double_sided.py`；
  结果和图在 `out\clothes_burst\`。
