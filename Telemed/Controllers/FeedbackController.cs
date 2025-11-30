using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Telemed.Models;
using System;

namespace Telemed.Controllers
{
    [Authorize]
    public class FeedbackController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FeedbackController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 🔹 PATIENT: view their own feedbacks
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> MyFeedbacks()
        {
            var patient = await _context.Patients
                .FirstOrDefaultAsync(p => p.UserId == User.Identity.Name || p.User.Email == User.Identity.Name);

            if (patient == null) return NotFound();

            var feedbacks = await _context.Feedbacks
                .Include(f => f.Doctor).ThenInclude(d => d.User)
                .Include(f => f.Appointment)
                .Where(f => f.PatientId == patient.PatientId)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            return View(feedbacks);
        }

        // 🔹 PATIENT: create new feedback (for completed appointment)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> Create(Feedback feedback)
        {
            // Make sure Rating is within valid range
            if (feedback.Rating < 1 || feedback.Rating > 5)
            {
                TempData["Error"] = "Please select a rating between 1 and 5.";
                return RedirectToAction("Index", "Appointments");
            }

            feedback.CreatedAt = DateTime.Now;
            _context.Feedbacks.Add(feedback);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(MyFeedbacks));
        }


        // 🔹 PATIENT: edit feedback
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> Edit(int id, int rating, string comment)
        {
            var existing = await _context.Feedbacks.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Rating = rating;
            existing.Comment = comment;
            existing.UpdatedAt = DateTime.Now;

            _context.Update(existing);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Feedback updated successfully!";
            return RedirectToAction("MyFeedbacks");
        }

        // 🔹 PATIENT: delete feedback
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> Delete(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback != null)
            {
                _context.Feedbacks.Remove(feedback);
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "Feedback deleted successfully!";
            return RedirectToAction("MyFeedbacks");
        }

        // 🔹 DOCTOR/ADMIN: view received feedback
        [Authorize(Roles = "Doctor,Admin")]
        public async Task<IActionResult> DoctorFeedbacks()
        {
            var doctor = await _context.Doctors
                .FirstOrDefaultAsync(d => d.UserId == User.Identity.Name || d.User.Email == User.Identity.Name);

            if (doctor == null) return NotFound();

            var feedbacks = await _context.Feedbacks
                .Include(f => f.Patient).ThenInclude(p => p.User)
                .Include(f => f.Appointment)
                .Where(f => f.DoctorId == doctor.DoctorId)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            return View(feedbacks);
        }
        // 🔹 ADMIN: view all patient feedback
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminFeedbacks()
        {
            var feedbacks = await _context.Feedbacks
                .Include(f => f.Patient).ThenInclude(p => p.User)
                .Include(f => f.Doctor).ThenInclude(d => d.User)
                .Include(f => f.Appointment)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            return View(feedbacks);
        }

    }
}
