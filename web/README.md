# 华清街大亨 Web Frontend

这是 THUAI-9 “华清街大亨”主题的原生 Web SPA，用于旁观者观战页、操盘手调试控制台和管理员服务器控制台。前端保留原有无框架 ES modules 结构，静态托管切换为 Node.js + Express。

## 运行

```bash
cd web
npm install
npm run serve
```

打开：

- `http://localhost:5173/?mode=observer`
- `http://localhost:5173/?mode=player&token=player1`
- `http://localhost:5173/?mode=admin&secret=YOUR_SECRET`

默认 WebSocket 服务端为 `ws://localhost:14514`。页面会优先发送 `HELLO`，服务端尚未支持握手时仍可进入 legacy 联调状态。

颜色配置默认采用“红涨绿跌 / 红买绿卖”，可在页面顶部切换为“绿涨红跌 / 绿买红卖”或色盲友好配色；选择会保存在浏览器本地。

## 本次 UI 改造

- 深色交易所主题：整体改为 GitHub/Bloomberg 风格的深色界面，主色为金色、红色、绿色。
- 顶部行情条：展示最新黄金成交和当前交易日/Tick。
- 侧边栏品牌区：新增 `sidebar-brand.png` 品牌图，图片缺失时自动降级为 CSS fallback。
- 背景动效：新增 `particles-bg` canvas，绘制低透明度金色粒子。
- 华清快报：弹窗样式重做，包含正常头部、内容区、空态和玩家快速研报入口。
- 结算弹窗：改为左侧人物插画、右侧结算数据的双栏布局，并加入入场和数字滚动动画。
- 事件流：新事件滑入，重要事件有边框脉冲。
- 价格闪烁：盘口与关键价格变动会按涨跌颜色短暂闪烁。
- K 线图发光：蜡烛和成交量柱加入 canvas glow。

## K 线交互

K 线图现在支持可视窗口操作：

- 鼠标或触控板纵向滚轮：连续缩放。
- 横向滚动或按住拖拽：左右平移。
- 切换 `10 / 20 / 50 / 100 Tick` 间隔：重新聚合 K 线，并重置为左对齐视图。
- 初始视图保持左对齐，不再把所有 K 线平均拉伸到整张图里。

## 美术资源

主题图片放在 `assets/`：

- `assets/sidebar-brand.png`：侧边栏品牌图。
- `assets/settlement-character.png`：结算弹窗人物插画。
- `assets/bg-texture.png`：全局背景纹理。

当前仓库内包含可直接使用的 PNG 占位资产，后续可用正式美术稿同名覆盖。图片缺失不会影响功能，前端会降级到纯色/渐变背景。

## 目录

- `index.html`：SPA 入口。
- `server.js`：Express 静态服务器，默认监听 `5173`。
- `styles.css`：深色主题、布局、动画与弹窗样式。
- `src/main.js`：启动、连接管理、表单事件。
- `src/store.js`：消息归约与集中状态。
- `src/candles.js`：K 线聚合。
- `src/render.js`：DOM、ticker、弹窗、事件流与 canvas 渲染。
- `src/actions.js`：Client -> Server 动作消息。
- `src/sample-data.js`：离线演示数据。
- `tests/candles.test.mjs`：K 线聚合单元测试。
- `assets/`：主题 PNG 资产和资源说明。

## 验证

```bash
cd web
npm test
```

页面内点击“演示数据”可离线验证 ticker、K 线、盘口、事件流、华清快报和结算弹窗等主要 UI。
