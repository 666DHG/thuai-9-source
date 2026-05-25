"""Main module for Python SDK"""

import asyncio
import argparse
import logging
import os

from sdk_python.agent import Agent
from sdk_python.models import (
    GameState,
    MarketState,
    News,
    PlayerState,
    Prediction,
    StrategyOptions,
)

logging.basicConfig(level=logging.INFO)


def parse_args():
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description="THUAI-9 Python SDK agent")
    parser.add_argument("--token", default=os.environ.get("TOKEN", "player1"))
    parser.add_argument(
        "--server", default=os.environ.get("SERVER", "ws://localhost:14514")
    )
    return parser.parse_args()


class MyAgent(Agent):
    """Minimal example agent for a complete two-player match."""

    def __init__(self, token: str, server_url: str):
        super().__init__(token, server_url)
        self._last_order_tick = -999

    async def on_game_state(self, state: GameState):
        logging.info(
            "Game: %s Day=%s Tick=%s",
            state.stage,
            state.current_day,
            state.current_tick,
        )

    async def on_market_state(self, state: MarketState):
        if state.tick - self._last_order_tick < 25:
            return

        if state.bids and self.player_state.gold > 0:
            await self.limit_sell(state.bids[0].price, 1)
            self._last_order_tick = state.tick
            return

        if state.asks and self.player_state.mora >= state.asks[0].price:
            await self.limit_buy(state.asks[0].price, 1)
            self._last_order_tick = state.tick

    async def on_player_state(self, state: PlayerState):
        pass  # Check your portfolio here

    async def on_news(self, news: News):
        logging.info("News [%s]: %s", news.news_id, news.content)
        # Example: submit a research report predicting Long
        await self.submit_report(news.news_id, Prediction.LONG)

    async def on_strategy_options(self, options: StrategyOptions):
        # Pick the first available card
        if options.infrastructure:
            await self.select_strategy(options.infrastructure.name)
        elif options.risk_control:
            await self.select_strategy(options.risk_control.name)
        elif options.fin_tech:
            await self.select_strategy(options.fin_tech.name)


async def main():
    """Async main function"""
    args = parse_args()
    token = args.token
    server = args.server
    agent = MyAgent(token=token, server_url=server)
    await agent.run()


if __name__ == "__main__":
    asyncio.run(main())
