namespace MiHouse.Core.Session;

/// <summary>
/// Authenticated Mi Home session material.
/// </summary>
/// <param name="UserId">Numeric Xiaomi account id used in request cookies.</param>
/// <param name="ServiceToken">Token for the <c>xiaomiio</c> service.</param>
/// <param name="Ssecurity">Secret used to derive request signatures.</param>
/// <param name="Region">Cloud region key: <c>cn</c>, <c>de</c>, <c>i2</c>, <c>ru</c>, <c>sg</c> or <c>us</c>.</param>
public sealed record MiHomeSession(string UserId, string ServiceToken, string Ssecurity, string Region);
