using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;

namespace Telemed.Controllers
{
    public class DoctorsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DoctorsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Doctors
        public async Task<IActionResult> Index(string searchTerm)
        {
            var doctorsQuery = _context.Doctors
                .Include(d => d.User)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                doctorsQuery = doctorsQuery.Where(d =>
                    d.User.FullName.ToLower().Contains(term) ||
                    d.Specialization.ToLower().Contains(term)
                );
            }

            var doctors = await doctorsQuery.ToListAsync();
            ViewBag.SearchTerm = searchTerm;
            return View(doctors);
        }

        // GET: Doctors/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(m => m.DoctorId == id);

            if (doctor == null)
                return NotFound();

            return View(doctor);
        }

        // GET: Doctors/Create
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("UserId,Specialization,Qualification,BMDCNumber,ConsultationFee,IsApproved")]
            Doctor doctor)
        {
            if (ModelState.IsValid)
            {
                _context.Add(doctor);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            var users = _context.Users
                .Where(u => !_context.Doctors.Any(d => d.UserId == u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToList();

            ViewBag.UserId = new SelectList(users, "Id", "FullName", doctor.UserId);
            return View(doctor);
        }

        // POST: Doctors/EditFromModal
        // This is used by the modal on Index.cshtml
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFromModal(
            int doctorId,
            string specialization,
            string qualification,
            string bmdcNumber,
            decimal consultationFee,
            bool isApproved)
        {
            var existingDoctor = await _context.Doctors
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId);

            if (existingDoctor == null)
                return NotFound();

            existingDoctor.Specialization = specialization?.Trim();
            existingDoctor.Qualification = string.IsNullOrWhiteSpace(qualification) ? null : qualification.Trim();
            existingDoctor.BMDCNumber = bmdcNumber?.Trim();
            existingDoctor.ConsultationFee = consultationFee;
            existingDoctor.IsApproved = isApproved;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Doctor details updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Doctors/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(m => m.DoctorId == id);

            if (doctor == null)
                return NotFound();

            return View(doctor);
        }

        // POST: Doctors/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var doctor = await _context.Doctors.FindAsync(id);
            if (doctor != null)
                _context.Doctors.Remove(doctor);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool DoctorExists(int id)
        {
            return _context.Doctors.Any(e => e.DoctorId == id);
        }
    }
}