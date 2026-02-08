using System;
using System.Collections.Generic;
using FrameWork;
using LauncherServer.Dtos;
using LauncherServer.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LauncherServer.Console
{
    [ConsoleHandler("state", 1, "Server State")]
    public class State : IConsoleHandler
    {
        private static readonly Lazy<ILogger<State>> _lazyLogger = new Lazy<ILogger<State>>(() =>
            Core.ServiceProvider.GetService<ILogger<State>>());
        private static ILogger<State> _logger => _lazyLogger.Value;
        
        public bool HandleCommand(string command, List<string> args)
        {
            ServerState State;

            if (!Enum.TryParse(args[0], out State))
            {
                _logger?.LogError("Invalid State");
                return false;
            }

            PatchMgr.SetServerState(State);
            _logger?.LogInformation("Server state is now {State}", State);

            return true;
        }
    }
}