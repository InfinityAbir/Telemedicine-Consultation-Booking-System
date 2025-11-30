using Microsoft.AspNetCore.Identity;
using Telemed.Models;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();

        string[] roles = new[] { "Admin", "Doctor", "Patient" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Helper local function to create user + confirm email
        async Task<ApplicationUser> CreateUserIfNotExists(string email, string password, string fullName, string userRole, Action<ApplicationUser>? afterCreate = null)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user != null) return user;

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                UserRole = userRole,
                IsActive = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                // You might want to log errors in real code
                return user;
            }

            await userManager.AddToRoleAsync(user, userRole);

            // Confirm email programmatically for seeded users (safe for dev)
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await userManager.ConfirmEmailAsync(user, token);

            afterCreate?.Invoke(user);

            return user;
        }

        // Create Admin
        var admin = await CreateUserIfNotExists("admin@telemed.local", "Admin@1234", "System Admin", "Admin");

        // Create Doctor and associated Doctor record
        var doctorUser = await CreateUserIfNotExists("doctor@telemed.local", "Doctor@1234", "Dr. John Doe", "Doctor", user =>
        {
            if (!dbContext.Doctors.Any(d => d.UserId == user.Id))
            {
                var doctor = new Doctor
                {
                    UserId = user.Id,
                    Specialization = "General Medicine",
                    Qualification = "MBBS",
                    IsApproved = true
                };
                dbContext.Doctors.Add(doctor);
                dbContext.SaveChanges();
            }
        });

        // Create Patient and associated Patient record
        var patientUser = await CreateUserIfNotExists("patient@telemed.local", "Patient@1234", "Jane Smith", "Patient", user =>
        {
            if (!dbContext.Patients.Any(p => p.UserId == user.Id))
            {
                var patient = new Patient
                {
                    UserId = user.Id,
                    DOB = new DateTime(1990, 1, 1),
                    Gender = "Female",
                    ContactNumber = "2222222222"
                };
                dbContext.Patients.Add(patient);
                dbContext.SaveChanges();
            }
        });
    }
}
