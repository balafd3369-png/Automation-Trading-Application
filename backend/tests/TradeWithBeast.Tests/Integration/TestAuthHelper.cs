using System.Net.Http.Headers;
using System.Net.Http.Json;
using TradeWithBeast.Models;

namespace TradeWithBeast.Tests.Integration;

internal static class TestAuthHelper
{
    public const string DefaultAdminPassword = "secure-integration-admin-12";

    public static async Task<string> LoginAdminAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new AuthLoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        return session!.Token;
    }

    public static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
