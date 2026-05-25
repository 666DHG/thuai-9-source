# Web 前端展示方案

本文档描述“璃月黄金交易所”Web 前端首期框架。前端以 WebSocket 为唯一实时数据源，优先支持观战大屏与选手调试控制台；结算页先作为页面内弹层实现。

## 目标

- 为裁判、直播和观众提供公共市场视角、比分、事件流和结算呈现。
- 为选手提供私有盘口视角、资产、挂单、策略卡、新闻研报和手动操作入口。
- 从后端 `MARKET_STATE` 逐 Tick 聚合 K 线，不要求后端首期提供 OHLC 数据。
- 严格区分公共状态与私有状态，避免 observer 泄漏选手完整挂单。

## 路由与模式

前端采用单页应用，首期提供两个模式：

- `/observer`：公共观战视角。展示真实盘口、双方摘要、事件解说、比分和结算。
- `/player`：选手控制台。需要 `token`，展示该玩家收到的私有视角，并开放手动动作面板。

也可以使用查询参数在静态部署中切换模式：

- `web/index.html?mode=observer`
- `web/index.html?mode=player&token=player1`

连接面板默认提供两个 WebSocket 地址选项：

- `ws://localhost:14514`
- `ws://59.66.135.18:14514`

其中本地连接允许修改 `localhost` 的端口，但不允许切换到其他主机名。

## Observer 页面

Observer 页面面向大屏和裁判台，默认不需要 token。

### 顶部状态条

- 连接状态。
- 当前阶段 `stage`。
- 当前交易日 `currentDay`。
- 全局 Tick `currentTick`。
- 日内 Tick `dayTick` 或 `MARKET_STATE.tick`。
- 比分 `scores`。

### 市场主视图

- K 线图：默认用 `midPrice` 聚合。
- 成交量柱图：用 `MARKET_STATE.volume` 的差分值。
- 价格摘要：best bid、best ask、spread、mid、last。
- 盘口：买一到买十、卖一到卖十。

### 事件流

按时间倒序展示：

- 新闻发布 `NEWS_BROADCAST`。
- 研报结算 `REPORT_RESULT`。
- 成交 `TRADE_NOTIFICATION`。
- 技能触发 `SKILL_EFFECT`。
- 系统错误或提示 `ERROR`。
- 结算 `DAY_SETTLEMENT`。

展示优先级建议：

1. 结算、最终冠军。
2. 技能触发、熔断类系统事件。
3. 新闻和研报结果。
4. 大额成交。
5. 普通成交和普通状态提示。

### 玩家对比

Observer 只使用 `PLAYER_SUMMARY_STATE` 或服务端明确允许公开的摘要字段：

- `token`。
- `mora` / `frozenMora`。
- `gold` / `frozenGold` / `lockedGold`。
- `nav`。
- `activeCards`。
- `pendingOrderCount`。
- 当日 `tradeCount`。

Observer 不展示每个玩家的完整 `pendingOrders`。

## Player 页面

Player 页面用于选手调试和演示，需要 token。

### 市场区

- K 线与盘口直接展示该连接收到的 `MARKET_STATE`。
- 如果对手触发“恶意做空”，当前后端会把伪卖盘混入被影响玩家的 `asks`，Player 页面不做额外过滤。

### 资产区

展示 `PLAYER_STATE`：

- `mora`、`frozenMora`。
- `gold`、`frozenGold`、`lockedGold`。
- `nav`。
- `activeCards`。

### 挂单区

展示 `pendingOrders` 表格：

- `orderId`。
- `side`。
- `price`。
- `quantity`。
- `remainingQuantity`。
- `status`。

### 策略区

- 在 `StrategySelection` 阶段显示 `STRATEGY_OPTIONS`。
- 支持选择 `infrastructure`、`riskControl`、`finTech` 任一候选卡。
- 已激活卡牌来自 `PLAYER_STATE.activeCards`。
- 若后端补充 `skillStates` 或 `activeCardStates`，再显示冷却与持续时间。

### 新闻与研报

- 新闻列表来自 `NEWS_BROADCAST`。
- 研报提交使用 `SUBMIT_REPORT`，方向为 `Long`、`Short`、`Hold`。
- 研报结算展示 `REPORT_RESULT`。

### 手动交易

Player 首期保留人工调试入口：

- 限价买入 `LIMIT_BUY`。
- 限价卖出 `LIMIT_SELL`。
- 撤单 `CANCEL_ORDER`。
- 激活技能 `ACTIVATE_SKILL`。

所有动作都必须带 `token`。后端升级到显式握手后，仍建议动作消息保留 token，便于日志和兼容旧 SDK。

## 组件清单

- `TopStatusBar`：阶段、交易日、Tick、比分、连接状态。
- `ConnectionPanel`：server、role、token、连接/断开。
- `MarketChartPanel`：K 线、成交量、价格口径切换。
- `OrderBookPanel`：买卖十档、spread、mid、last。
- `EventFeedPanel`：新闻、成交、研报、技能、错误。
- `PortfolioPanel`：Player 私有资产。
- `PlayerComparisonPanel`：Observer 玩家摘要对比。
- `PendingOrdersTable`：Player 当前挂单。
- `StrategyOptionsPanel`：策略候选卡与选择动作。
- `OrderEntryPanel`：限价单与撤单。
- `ReportSubmitPanel`：新闻研报。
- `SkillActionPanel`：主动技能触发。
- `ScoreboardPanel`：比分。
- `SettlementModal`：单日结算与最终结果。

## 状态分层

建议前端 store 分为：

- `connection`：WebSocket 状态、role、token、server、重连次数。
- `game`：`stage`、`currentDay`、`currentTick`、`dayTick`、`dayTickLimit`、`scores`。
- `market`：最新 `MARKET_STATE`、盘口、K 线、成交量 baseline。
- `players`：Player 私有 `PLAYER_STATE`，Observer 公共 `PLAYER_SUMMARY_STATE`。
- `strategy`：`STRATEGY_OPTIONS`、已选/已激活卡、技能状态。
- `events`：新闻、成交、研报结果、技能效果、错误、结算事件。
- `ui`：当前路由模式、价格口径、K 线 interval、弹层状态。

## WebSocket 生命周期

目标协议：

1. 建立 WebSocket 连接。
2. 发送 `HELLO`。
3. 收到 `PLAYER_STATE`（包含分配的 `playerId`）。
4. 持续接收 Tick snapshot 与 event。
5. 断线后指数退避重连，重连后重新 `HELLO`。

后端已完整实现 `HELLO` 握手和 Observer / Admin 角色支持，前端通过 `HELLO` 即可接入。

Player 握手：

```json
{
  "messageType": "HELLO",
  "role": "player",
  "token": "player1",
  "protocolVersion": "v1"
}
```

Observer 握手：

```json
{
  "messageType": "HELLO",
  "role": "observer",
  "protocolVersion": "v1"
}
```

## K 线聚合

后端当前广播的是逐 Tick 市场快照，前端自行聚合 OHLC。

内部结构：

```ts
type Candle = {
  day: number
  bucketStartTick: number
  bucketEndTick: number
  open: number
  high: number
  low: number
  close: number
  volume: number
}
```

规则：

1. 默认价格口径为 `midPrice`，可切换 `lastPrice`。
2. 默认 20 Tick 一根 K 线，可切换 10 / 20 / 50 / 100 Tick。
3. 交易日取 `GAME_STATE.currentDay`。
4. 日内 Tick 取 `MARKET_STATE.tick`。
5. `bucket = floor((tick - 1) / interval)`。
6. 新桶第一条：`open = high = low = close = price`。
7. 同桶更新：`high = max(high, price)`、`low = min(low, price)`、`close = price`。
8. `MARKET_STATE.volume` 是当日累计成交量，柱图使用差分 `max(0, currentVolume - previousVolume)`。
9. 跨日、Tick 回退、断线重连后的第一条 snapshot 只重置 volume baseline，不计入 delta。
10. 非 `TradingDay` 阶段不生成 candle。

## 公共与私有边界

- `GAME_STATE`、公共新闻、策略候选、技能效果适合全局广播。
- `PLAYER_STATE` 是私有状态，只发给对应玩家。
- `PLAYER_SUMMARY_STATE` 是 observer 摘要，不包含完整挂单列表。
- `MARKET_STATE` 在 player 视角可能包含技能造成的伪盘口；observer 应接收公共真实盘口。
- `REPORT_RESULT` 在 player 侧可不带 `playerToken`，observer 侧建议补充 `playerToken`。

## 结算展示

`DAY_SETTLEMENT` 建议字段：

- `day`。
- `winnerToken`。
- `reason`：`NAV`、`TradeCount` 或 `Tie`。
- `scores`。
- `players[].token`。
- `players[].nav`。
- `players[].tradeCount`。
- `players[].mora`、`players[].gold` 可选。

最终结算可复用 `DAY_SETTLEMENT` 的 `scores`，在 `GAME_STATE.stage = "Finished"` 后展示冠军。

## 当前前端骨架

仓库中的 `web/` 目录是一个无构建依赖的 SPA 骨架：

- `web/index.html`：应用入口。
- `web/styles.css`：响应式仪表盘样式。
- `web/src/main.js`：启动、事件绑定、WebSocket 连接。
- `web/src/store.js`：集中状态与消息归约。
- `web/src/candles.js`：K 线聚合。
- `web/src/render.js`：DOM 渲染。
- `web/src/actions.js`：Player 动作消息。
- `web/src/sample-data.js`：离线演示数据。

可用任意静态服务器打开，例如：

```bash
python3 -m http.server 5173 -d web
```
