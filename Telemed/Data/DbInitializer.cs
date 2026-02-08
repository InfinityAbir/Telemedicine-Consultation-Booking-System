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

        async Task<ApplicationUser> CreateUserIfNotExists(
            string email,
            string password,
            string fullName,
            string userRole,
            Func<ApplicationUser, Task>? afterCreate = null)
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
                return user;

            await userManager.AddToRoleAsync(user, userRole);

            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await userManager.ConfirmEmailAsync(user, token);

            if (afterCreate != null)
                await afterCreate(user);

            return user;
        }

        // Admin
        var admin = await CreateUserIfNotExists(
            "admin@telemed.local",
            "Admin@1234",
            "System Admin",
            "Admin"
        );

        // Doctor + Doctor profile
        var doctorUser = await CreateUserIfNotExists(
            "doctor@telemed.local",
            "Doctor@1234",
            "Dr. John Doe",
            "Doctor",
            async user =>
            {
                if (!dbContext.Doctors.Any(d => d.UserId == user.Id))
                {
                    dbContext.Doctors.Add(new Doctor
                    {
                        UserId = user.Id,
                        Specialization = "General Medicine",
                        Qualification = "MBBS",
                        IsApproved = true
                    });
                }
            }
        );

        // Patient + Patient profile
        var patientUser = await CreateUserIfNotExists(
            "patient@telemed.local",
            "Patient@1234",
            "Jane Smith",
            "Patient",
            async user =>
            {
                if (!dbContext.Patients.Any(p => p.UserId == user.Id))
                {
                    dbContext.Patients.Add(new Patient
                    {
                        UserId = user.Id,
                        DOB = new DateTime(1990, 1, 1),
                        Gender = "Female",
                        ContactNumber = "2222222222"
                    });
                }
            }
        );

        await dbContext.SaveChangesAsync();
    }
}
