using System.Text;
using Microsoft.AspNetCore.Identity;
using Platform.Web.Areas.Admin.Data;

namespace Platform.Web.Areas.Admin;

public static class AdminCli
{
    private const int MinimumPasswordLength = 12;

    /// <summary>`dotnet run -- create-admin you@example.com`: creates the admin, or resets the password if it exists.</summary>
    public static async Task<int> CreateAdminAsync(AdminUserRepository users, string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            Console.Error.WriteLine("Usage: dotnet run -- create-admin you@example.com");
            return 1;
        }

        var password = ReadPassword($"Password for {email} (at least {MinimumPasswordLength} characters): ");
        if (password.Length < MinimumPasswordLength)
        {
            Console.Error.WriteLine($"The password must be at least {MinimumPasswordLength} characters.");
            return 1;
        }

        if (ReadPassword("Repeat the password: ") != password)
        {
            Console.Error.WriteLine("The passwords do not match.");
            return 1;
        }

        email = email.Trim();
        await users.SaveAsync(email, new PasswordHasher<string>().HashPassword(email, password));
        Console.WriteLine($"Admin {email} saved. Sign in at /admin.");
        return 0;
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);

        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? "";

        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return password.ToString();
    }
}
