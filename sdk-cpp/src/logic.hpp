#pragma once

#include <memory>
#include <string>

#include "agent.hpp"

auto createAgent(std::string token, std::string serverUrl)
    -> std::unique_ptr<thuai::Agent>;
