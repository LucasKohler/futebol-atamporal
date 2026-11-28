using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FutebolAtemporal.Api.Tests;

/// <summary>Credenciais fictícias só para testes; o código de configuração chega como segredo de ambiente.</summary>
internal static class TestAuth
{
    public const string Password = "Senha1234";
    public const string SetupCode = "codigo-de-configuracao-teste-2026";

    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("Seed__AdminSetupCode", SetupCode);

    /// <summary>Garante a senha do primeiro admin (uso único; depois responde 409/400) e faz o login.</summary>
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string phone)
    {
        using var _ = await client.PostAsJsonAsync("/api/auth/admin-setup", new { setupCode = SetupCode, phone, password = Password });
        return await client.PostAsJsonAsync("/api/auth/login", new { phone, password = Password });
    }

    /// <summary>
    /// POST /api/notifications/subscriptions com a precondição <c>expectedUserId</c> = usuário da sessão atual do cliente.
    /// </summary>
    public static async Task<HttpResponseMessage> SubscribePushAsync(HttpClient client, object body)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var json = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        json["expectedUserId"] = me.GetProperty("id").GetGuid().ToString();
        return await client.PostAsJsonAsync("/api/notifications/subscriptions", json);
    }
}
