# v0.5 美术记录

生成方式：内置 imagegen。参考输入为本项目 forest-battlefield-v04.png，仅用作色彩/笔触/俯视角参考，不使用《王国保卫战》游戏素材。

输出：Assets/Resources/Art/enemies-v05.png + enemy-regions-v05.json；1448×1086，12个互不重叠的独立区域。原始生成图为RGB并带浅灰棋盘格；用户已明确回复“允许本地抠图”。本地移除近白中性背景、保留12个主体连通区域，输出真实RGBA（1,056,492透明像素），不更换怪物设计，不把画出来的棋盘格当透明。

已通过Unity真实窗口检查：两页图鉴、战斗中苔团/野猪/甲虫，脚底落在同一条路上。美术仍为静态占位。

## 完整生成提示词

```text
Use case: stylized-concept. Asset type: production 2D enemy sprite atlas for a Unity forest pet tower defense game. The supplied forest map is STYLE REFERENCE ONLY: match its warm upper-left light, muted organic woodland colors, hand-painted matte cartoon forms, bold dark brown outlines and slightly elevated 3/4 camera. Original friendly-menacing medieval fantasy enemies, Kingdom Rush-like readability, not copied characters. Produce ONE sprite sheet, exactly FOUR columns by THREE rows, 12 isolated full-body characters each contained in its equal cell with wide transparent gutters and complete feet/weapons. Consistent perspective facing down-right, clear readable silhouettes at 50-90 screen pixels. Genuine TRANSPARENT PNG background (alpha zero), NOT a drawn checkerboard, no scene, no floor, no cast shadow, no labels or grid lines. Each sprite around 70% cell size; NEVER touch cell borders.
Row1 left to right: 1 squat moss-green slime with two eyes and tiny sprout; 2 rust-brown charging wild boar with curved ivory tusks; 3 dark blue-gray armored stag beetle with ivory horn and large segmented shell; 4 towering ancient stone ogre boss with moss shoulders, violet crystal crown, massive stony fists.
Row2 left to right: 5 thin gray forest wolf scout in running pose; 6 small goblin shield guard carrying huge brass-rimmed round blue shield and leather helmet; 7 short mushroom healer shaman with broad red mushroom cap, small face and wooden staff holding glowing lime-green seed; 8 amber gelatinous brood slime containing two distinct baby slime faces, wider than first slime.
Row3 left to right: 9 very small lime slime child, single leaf on head, visibly simpler baby silhouette; 10 bulky dark brown rage bear standing on hind legs with red warpaint and claws; 11 stocky gray boulder golem soldier, rugged square silhouette, moss cracks and tiny amber eyes (NOT a copy of the boss); 12 tusked boar chieftain boss upright in leather plate armor holding stone axe, rough wooden crown, dark russet mane.
Avoid glossy 3D toy rendering, shiny jelly, excessive detail, lettering, decorative backgrounds, contact shadows, checkerboard pixels. The atlas must serve as actual clean sprites over the supplied map, not an infographic or concept poster.
```

