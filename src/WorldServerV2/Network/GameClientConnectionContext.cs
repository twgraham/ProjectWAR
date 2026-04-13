using Core.Infrastructure.Network;
using Core.Session;

namespace WorldServerV2.Network;

public static class GameClientConnectionContext
{
    extension(IConnectionContext context)
    {
        public string ClientId => context.Get<string>("ClientId") ?? "UnknownClient";

        /// <summary>
        /// The <see cref="GameSession"/> for this connection, or <c>null</c> if not yet created.
        /// Set automatically by <see cref="SessionLifecycleService"/> on connect.
        /// </summary>
        public GameSession Session
            => context.TryGetValue<GameSession>(SessionRegistry.ConnectionItemKey, out var session)
                ? session
                : throw new InvalidOperationException("GameSession not found in connection context. Ensure SessionLifecycleService is properly configured.");

        public AccountInfo? Account
        {
            get => context.TryGetValue<AccountInfo>("Account", out var account) ? account : null;
            set
            {
                if (value == null)
                    context.Items.Remove("Account");
                else
                    context.Items["Account"] = value;
            }
        }
    }
}