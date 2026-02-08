using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;

namespace Telemed.Controllers
{
    [Authorize]
    public class DoctorsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DoctorsController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// GET: Doctors
        [AllowAnonymous]
        public async Task<IActionResult> Index(string searchTerm)
        {
            var doctorsQuery = _context.Doctors
                .Include(d => d.User)
                .AsQueryable();

            // Non-admins and guests should only see approved doctors
            if (!User.IsInRole("Admin"))
            {
                doctorsQuery = doctorsQuery.Where(d => d.IsApproved);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                doctorsQuery = doctorsQuery.Where(d =>
                    d.User.FullName.ToLower().Contains(term) ||
                    d.Specialization.ToLower().Contains(term)
                );
            }

            ViewBag.SearchTerm = searchTerm;
            return View(await doctorsQuery.ToListAsync());
        }


        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.DoctorId == id);

            if (doctor == null) return NotFound();

            return View(doctor);
        }


        // GET: Doctors/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            var users = _context.Users
                .Where(u => !_context.Doctors.Any(d => d.UserId == u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToList();

            ViewBag.UserId = new SelectList(users, "Id", "FullName");
            return View();
        }

        // POST: Doctors/Create
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("UserId,Specialization,Qualification,BMDCNumber,ConsultationFee")]
            Doctor doctor)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.UserId = new SelectList(
                    _context.Users.Select(u => new { u.Id, u.FullName }),
                    "Id",
                    "FullName",
                    doctor.UserId
                );
                return View(doctor);
            }

            doctor.IsApproved = true; // Admin created → auto approved
            _context.Doctors.Add(doctor);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // POST: Doctors/EditFromModal
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFromModal(
            int doctorId,
            string specialization,
            string qualification,
            string bmdcNumber,
            decimal consultationFee)
        {
            var doctor = await _context.Doctors
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId);

            if (doctor == null) return NotFound();

            doctor.Specialization = specialization?.Trim();
            doctor.Qualification = string.IsNullOrWhiteSpace(qualification) ? null : qualification.Trim();
            doctor.BMDCNumber = bmdcNumber?.Trim();

            var isAdmin = User.IsInRole("Admin");
            var isDoctor = User.IsInRole("Doctor");

            if (isAdmin)
            {
                // Admin → direct update
                doctor.ConsultationFee = consultationFee;
                doctor.PendingConsultationFee = null;
                doctor.IsApproved = true;

                TempData["Success"] = "Consultation fee updated successfully.";
            }
            else if (isDoctor)
            {
                // Doctor → request approval
                doctor.PendingConsultationFee = consultationFee;
                doctor.IsApproved = false;

                TempData["Success"] = "Consultation fee change submitted for admin approval.";
            }
            else
            {
                return Forbid();
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: Doctors/ApproveFee/5
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveFee(int id)
        {
            var doctor = await _context.Doctors.FindAsync(id);
            if (doctor == null || doctor.PendingConsultationFee == null)
                return NotFound();

            doctor.ConsultationFee = doctor.PendingConsultationFee.Value;
            doctor.PendingConsultationFee = null;
            doctor.IsApproved = true;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Consultation fee approved successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Doctors/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.DoctorId == id);

            if (doctor == null) return NotFound();

            return View(doctor);
        }

        // POST: Doctors/Delete/5
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var doctor = await _context.Doctors.FindAsync(id);
            if (doctor != null)
                _context.Doctors.Remove(doctor);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
