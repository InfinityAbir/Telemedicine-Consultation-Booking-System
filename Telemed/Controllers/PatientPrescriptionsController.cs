using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

[Authorize]
public class PatientPrescriptionsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public PatientPrescriptionsController(ApplicationDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    // GET: /PatientPrescriptions
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value;
        var list = await _db.PatientPrescriptionUploads
            .Where(p => p.PatientId == userId)
            .OrderByDescending(p => p.UploadDate)
            .ToListAsync();

        return View(list);
    }

    // GET: /PatientPrescriptions/Upload
    public IActionResult Upload() => View();

    // POST: /PatientPrescriptions/Upload
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, string description)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("file", "Please select a file.");
            return View();
        }

        const long maxFileBytes = 10 * 1024 * 1024;
        if (file.Length > maxFileBytes)
        {
            ModelState.AddModelError("file", "File is too large. Maximum 10 MB allowed.");
            return View();
        }

        var permittedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !permittedExtensions.Contains(ext))
        {
            ModelState.AddModelError("file", "Unsupported file type. Allowed: PDF, JPG, PNG.");
            return View();
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value;

        var relativeFolder = _config["FileStorage:PrescriptionsFolder"] ?? "App_Data/PatientPrescriptions";
        var storageFolder = Path.Combine(_env.ContentRootPath, relativeFolder);
        if (!Directory.Exists(storageFolder)) Directory.CreateDirectory(storageFolder);

        var storedFileName = $"{Guid.NewGuid()}{ext}";
        var savedPath = Path.Combine(storageFolder, storedFileName);

        using (var stream = new FileStream(savedPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var upload = new PatientPrescriptionUpload
        {
            PatientId = userId,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            UploadDate = DateTime.UtcNow,
            ContentType = file.ContentType,
            FileSize = file.Length,
            Description = description
        };

        _db.PatientPrescriptionUploads.Add(upload);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "File uploaded successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /PatientPrescriptions/Download/5
    public async Task<IActionResult> Download(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value;
        var record = await _db.PatientPrescriptionUploads.FindAsync(id);
        if (record == null) return NotFound();

        if (record.PatientId != userId && !User.IsInRole("Doctor") && !User.IsInRole("Admin"))
            return Forbid();

        var relativeFolder = _config["FileStorage:PrescriptionsFolder"] ?? "App_Data/PatientPrescriptions";
        var storageFolder = Path.Combine(_env.ContentRootPath, relativeFolder);
        var path = Path.Combine(storageFolder, record.StoredFileName);

        if (!System.IO.File.Exists(path)) return NotFound();

        var fs = System.IO.File.OpenRead(path);
        return File(fs, record.ContentType ?? "application/octet-stream", record.OriginalFileName);
    }

    // GET: /PatientPrescriptions/Preview/5
    [Authorize]
    public async Task<IActionResult> Preview(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value;
        var record = await _db.PatientPrescriptionUploads.FindAsync(id);
        if (record == null) return NotFound();

        // Only owner or doctor/admin can view
        if (record.PatientId != userId && !User.IsInRole("Doctor") && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var relativeFolder = _config["FileStorage:PrescriptionsFolder"] ?? "App_Data/PatientPrescriptions";
        var storageFolder = Path.Combine(_env.ContentRootPath, relativeFolder);
        var path = Path.Combine(storageFolder, record.StoredFileName);

        if (!System.IO.File.Exists(path)) return NotFound();

        var fs = System.IO.File.OpenRead(path);

        // Return file inline instead of forcing download
        return File(fs, record.ContentType ?? "application/octet-stream");
    }


    // POST: /PatientPrescriptions/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value;
        var record = await _db.PatientPrescriptionUploads.FindAsync(id);
        if (record == null) return NotFound();

        if (record.PatientId != userId) return Forbid();

        var relativeFolder = _config["FileStorage:PrescriptionsFolder"] ?? "App_Data/PatientPrescriptions";
        var storageFolder = Path.Combine(_env.ContentRootPath, relativeFolder);
        var path = Path.Combine(storageFolder, record.StoredFileName);

        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

        _db.PatientPrescriptionUploads.Remove(record);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "File removed.";
        return RedirectToAction(nameof(Index));
    }
}
