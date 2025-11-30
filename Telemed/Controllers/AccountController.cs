using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;
using Telemed.ViewModels;

namespace Telemed.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailSender _emailSender; // Email sender service

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _context = context;
            _environment = environment;
            _emailSender = emailSender;
        }

        // ---------------- LOGIN / REGISTER / LOGOUT ----------------
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe = false)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Email and password are required.");
                return View();
            }

            var trimmedEmail = email.Trim();
            var user = await _userManager.FindByEmailAsync(trimmedEmail);
            if (user == null)
            {
                ModelState.AddModelError("", "Invalid login attempt.");
                return View();
            }

            // block login if email not confirmed
            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                ModelState.AddModelError("", "You need to confirm your email before logging in. Check your inbox or request a new confirmation email.");
                return View();
            }

            if (await _userManager.IsInRoleAsync(user, "Doctor"))
            {
                var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id);
                if (doctor != null && !doctor.IsApproved)
                {
                    ModelState.AddModelError("", "Your account is pending admin approval.");
                    return View();
                }
            }

            var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, false);
            if (result.Succeeded)
            {
                if (await _userManager.IsInRoleAsync(user, "Admin"))
                    return RedirectToAction("PendingDoctors", "Admin");

                return RedirectToAction("Profile");
            }

            ModelState.AddModelError("", "Invalid login attempt.");
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Register() => View();

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            if ((model.Bio?.Length ?? 0) > 1000)
            {
                ModelState.AddModelError(nameof(model.Bio), "Bio / clinic address cannot exceed 1000 characters.");
                return View(model);
            }

            // normalize phone helper
            string NormalizePhone(string? phone)
            {
                if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
                var digits = new System.Text.StringBuilder();
                foreach (var ch in phone)
                    if (char.IsDigit(ch)) digits.Append(ch);
                return digits.ToString();
            }

            // Phone number check
            var normalizedPhone = NormalizePhone(model.ContactNumber);

            if (!string.IsNullOrEmpty(normalizedPhone))
            {
                // Check against AspNetUsers.PhoneNumber (we will store normalized digits there)
                var existingUserByPhone = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone);

                if (existingUserByPhone != null)
                {
                    ModelState.AddModelError(nameof(model.ContactNumber), "This contact number is already registered.");
                    return View(model);
                }

                // Check Patients table if needed (also store normalized there)
                var existingPatient = await _context.Patients
                    .FirstOrDefaultAsync(p => p.ContactNumber == normalizedPhone);

                if (existingPatient != null)
                {
                    ModelState.AddModelError(nameof(model.ContactNumber), "This contact number is already registered.");
                    return View(model);
                }
            }

            var email = model.Email?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                ModelState.AddModelError(nameof(model.Email), "Email is required.");
                return View(model);
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                ModelState.AddModelError(nameof(model.Email), "This email is already registered.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                FullName = model.FullName,
                Email = email,
                UserName = email,
                PhoneNumber = string.IsNullOrEmpty(normalizedPhone) ? null : normalizedPhone, // store normalized digits
                Address = model.Bio
            };

            var createResult = await _userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                    ModelState.AddModelError("", error.Description);
                return View(model);
            }

            if (!await _roleManager.RoleExistsAsync("Doctor"))
                await _roleManager.CreateAsync(new IdentityRole("Doctor"));
            if (!await _roleManager.RoleExistsAsync("Patient"))
                await _roleManager.CreateAsync(new IdentityRole("Patient"));

            if (model.RegisterAs == "Doctor")
            {
                await _userManager.AddToRoleAsync(user, "Doctor");
                var doctor = new Doctor
                {
                    UserId = user.Id,
                    Specialization = model.Specialization,
                    BMDCNumber = model.BMDCNumber,
                    Qualification = model.Qualification,
                    IsApproved = false,
                    ConsultationFee = model.ConsultationFee ?? 0 // fallback to 0 if null
                };
                _context.Doctors.Add(doctor);
            }
            else
            {
                await _userManager.AddToRoleAsync(user, "Patient");
                var patient = new Patient
                {
                    UserId = user.Id,
                    DOB = model.DOB,
                    Gender = model.Gender,
                    ContactNumber = string.IsNullOrEmpty(normalizedPhone) ? model.ContactNumber : normalizedPhone // store normalized if available
                };
                _context.Patients.Add(patient);
            }

            await _context.SaveChangesAsync();

            // Generate email confirmation token and send email (encoded safely)
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code = code }, protocol: Request.Scheme);

            var htmlMessage = $@"
        <div style='font-family: Arial, sans-serif; max-width:600px;'>
            <h2>Confirm your TeleMed account</h2>
            <p>Hello {HtmlEncoder.Default.Encode(user.FullName ?? user.Email)},</p>
            <p>Please confirm your email by clicking the button below:</p>
            <p style='text-align:center; margin:30px 0;'>
                <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style='display:inline-block;padding:12px 18px;background:#007bff;color:#fff;border-radius:8px;text-decoration:none;font-weight:600;'>Confirm email</a>
            </p>
            <p>If you didn't create an account, just ignore this email.</p>
            <p>Thanks,<br/>TeleMed Team</p>
        </div>";

            try
            {
                await _emailSender.SendEmailAsync(user.Email, "Confirm your TeleMed account", htmlMessage);
                TempData["Message"] = "Registration successful! We sent a confirmation email. If you didn’t receive it, request another below.";
            }
            catch (Exception)
            {
                TempData["Message"] = "Registration succeeded, but we couldn't send the confirmation email. Please use the resend option below.";
            }

            return RedirectToAction("ResendConfirmation", "Account");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        // ---------------- PROFILE (GET & POST) ----------------
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            if (await _userManager.IsInRoleAsync(user, "Doctor"))
            {
                var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id);
                ViewBag.Specialization = doctor?.Specialization;
                ViewBag.Qualification = doctor?.Qualification;
                ViewBag.BMDCNumber = doctor?.BMDCNumber;
                ViewBag.IsApproved = doctor?.IsApproved;
                ViewBag.ConsultationFee = doctor?.ConsultationFee ?? 0;
            }
            else if (await _userManager.IsInRoleAsync(user, "Patient"))
            {
                var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id);
                ViewBag.DOB = patient?.DOB?.ToString("yyyy-MM-dd");
                ViewBag.Gender = patient?.Gender;
            }

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ApplicationUser model, decimal? ConsultationFee)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            user.FullName = model.FullName;
            user.Address = model.Address;
            user.PhoneNumber = model.PhoneNumber;

            if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
            {
                user.Email = model.Email;
                user.UserName = model.Email;
            }

            var result = await _userManager.UpdateAsync(user);

            if (await _userManager.IsInRoleAsync(user, "Doctor"))
            {
                var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id);
                if (doctor != null)
                {
                    if (ConsultationFee.HasValue)
                        doctor.ConsultationFee = ConsultationFee.Value;

                    if (Request.Form.ContainsKey("BMDCNumber"))
                        doctor.BMDCNumber = Request.Form["BMDCNumber"];

                    _context.Doctors.Update(doctor);
                    await _context.SaveChangesAsync();
                }
            }

            TempData["Message"] = result.Succeeded ? "Profile updated successfully!" : "Failed to update profile.";
            return RedirectToAction("Profile");
        }

        // ---------------- PROFILE IMAGE ----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadProfileImage(IFormFile ProfileImage)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            if (ProfileImage != null && ProfileImage.Length > 0)
            {
                // Basic validation
                var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var ext = Path.GetExtension(ProfileImage.FileName).ToLowerInvariant();
                if (!allowedExt.Contains(ext))
                {
                    TempData["Message"] = "Only JPG, PNG or WEBP images are allowed.";
                    return RedirectToAction("Profile");
                }

                const long maxBytes = 2 * 1024 * 1024; // 2 MB
                if (ProfileImage.Length > maxBytes)
                {
                    TempData["Message"] = "Image must be under 2 MB.";
                    return RedirectToAction("Profile");
                }

                string uploadsFolder = Path.Combine(_environment.WebRootPath, "images");
                Directory.CreateDirectory(uploadsFolder);

                string fileName = $"{Guid.NewGuid()}{ext}";
                string filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfileImage.CopyToAsync(stream);
                }

                // Optionally: delete old image file (if not default)
                if (!string.IsNullOrEmpty(user.ProfileImageUrl) && !user.ProfileImageUrl.Contains("default-user.png"))
                {
                    var oldPath = Path.Combine(_environment.WebRootPath, user.ProfileImageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    try { if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath); } catch { /* ignore */ }
                }

                user.ProfileImageUrl = "/images/" + fileName;
                await _userManager.UpdateAsync(user);
                TempData["Message"] = "Profile picture updated successfully!";
            }
            else
            {
                TempData["Message"] = "Please select an image to upload.";
            }

            return RedirectToAction("Profile");
        }


        // ---------------- CHANGE PASSWORD ----------------
        [HttpPost]
        public async Task<IActionResult> ChangePassword(string CurrentPassword, string NewPassword, string ConfirmPassword)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            if (NewPassword != ConfirmPassword)
            {
                TempData["Message"] = "New password and confirmation do not match.";
                return RedirectToAction("Profile");
            }

            var result = await _userManager.ChangePasswordAsync(user, CurrentPassword, NewPassword);
            TempData["Message"] = result.Succeeded ? "Password changed successfully!" : "Failed to change password.";
            return RedirectToAction("Profile");
        }

        // ---------------- FORGOT PASSWORD ----------------
        [AllowAnonymous]
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                TempData["Message"] = "Please enter your email.";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                TempData["Message"] = "If an account with this email exists, a reset link will be sent.";
                return View();
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetLink = Url.Action("ResetPassword", "Account",
                new { email = user.Email, token = token }, Request.Scheme);

            // Modern HTML email
            var htmlMessage = $@"
    <!DOCTYPE html>
    <html lang='en'>
    <head>
        <meta charset='UTF-8'>
        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
        <title>Reset Password - TeleMed</title>
        <style>
            body {{ font-family: Arial, sans-serif; background-color: #f4f4f7; margin: 0; padding: 0; }}
            .email-container {{ max-width: 600px; margin: 30px auto; background-color: #fff; padding: 30px; border-radius: 10px; box-shadow: 0 4px 15px rgba(0,0,0,0.1); }}
            .logo {{ font-size: 2rem; font-weight: bold; color: #007bff; text-align: center; margin-bottom: 20px; }}
            .content {{ font-size: 16px; color: #333; line-height: 1.5; }}
            .btn {{ display: inline-block; padding: 12px 20px; margin: 20px 0; background-color: #007bff; color: #fff !important; text-decoration: none; border-radius: 8px; font-weight: bold; }}
            .footer {{ font-size: 12px; color: #999; text-align: center; margin-top: 20px; }}
        </style>
    </head>
    <body>
        <div class='email-container'>
            <div class='logo'>TeleMed</div>
            <div class='content'>
                <p>Hello {user.FullName},</p>
                <p>You have requested to reset your password for your TeleMed account.</p>
                <p style='text-align:center;'>
                    <a href='{HtmlEncoder.Default.Encode(resetLink)}' class='btn'>Reset Password</a>
                </p>
                <p>If you did not request this, please ignore this email.</p>
                <p>Thanks,<br>TeleMed Team</p>
            </div>
            <div class='footer'>&copy; {DateTime.Now.Year} TeleMed. All rights reserved.</div>
        </div>
    </body>
    </html>";

            // Send email
            await _emailSender.SendEmailAsync(user.Email, "Reset Password - TeleMed", htmlMessage);

            TempData["Message"] = "If an account with this email exists, a reset link will be sent.";
            return View();
        }

        // ---------------- RESET PASSWORD ----------------
        [AllowAnonymous]
        [HttpGet]
        public IActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null) return RedirectToAction("Login");
            var model = new ResetPasswordViewModel { Token = token, Email = email };
            return View(model);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                TempData["Message"] = "Password reset successful.";
                return RedirectToAction("Login");
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (result.Succeeded)
            {
                TempData["Message"] = "Password reset successful.";
                return RedirectToAction("Login");
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(model);
        }

        // ---------------- EMAIL CONFIRMATION ----------------
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                TempData["Message"] = "Invalid confirmation link.";
                return RedirectToAction("Login");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Message"] = "User not found.";
                return RedirectToAction("Login");
            }

            try
            {
                var decodedBytes = WebEncoders.Base64UrlDecode(code);
                var decodedToken = Encoding.UTF8.GetString(decodedBytes);
                var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
                if (result.Succeeded)
                {
                    TempData["Message"] = "Email confirmed successfully. You can now log in.";
                    return RedirectToAction("Login");
                }

                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                TempData["Message"] = $"Email confirmation failed: {errors}";
                return RedirectToAction("Login");
            }
            catch
            {
                TempData["Message"] = "Invalid confirmation token.";
                return RedirectToAction("Login");
            }
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ResendConfirmation() => View(new ResendConfirmationViewModel());

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmation(ResendConfirmationViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var trimmedEmail = model.Email.Trim();
            var user = await _userManager.FindByEmailAsync(trimmedEmail);
            if (user == null)
            {
                // Generic message to avoid revealing existence
                TempData["Message"] = "If an account with this email exists, a confirmation message has been sent.";
                return RedirectToAction("Login");
            }

            if (await _userManager.IsEmailConfirmedAsync(user))
            {
                TempData["Message"] = "This email is already confirmed. You can log in.";
                return RedirectToAction("Login");
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code = code }, protocol: Request.Scheme);

            var htmlMessage = $@"
                <div style='font-family: Arial, sans-serif; max-width:600px;'>
                    <h2>Confirm your TeleMed account</h2>
                    <p>Hello {HtmlEncoder.Default.Encode(user.FullName ?? user.Email)},</p>
                    <p>Please confirm your email by clicking the button below:</p>
                    <p style='text-align:center; margin:30px 0;'>
                        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style='display:inline-block;padding:12px 18px;background:#007bff;color:#fff;border-radius:8px;text-decoration:none;font-weight:600;'>Confirm email</a>
                    </p>
                    <p>If you didn't create an account, just ignore this email.</p>
                    <p>Thanks,<br/>TeleMed Team</p>
                </div>";

            await _emailSender.SendEmailAsync(user.Email, "Confirm your TeleMed account", htmlMessage);

            TempData["Message"] = "If an account with this email exists, a confirmation message has been sent.";
            return RedirectToAction("Login");
        }
    }
}
