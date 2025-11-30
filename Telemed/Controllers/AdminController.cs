using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Telemed.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ---------------------------
        // Dashboard
        // ---------------------------
        public async Task<IActionResult> Index()
        {
            var doctors = await _context.Doctors.ToListAsync();
            var patients = await _context.Patients.ToListAsync();
            var appointments = await _context.Appointments.ToListAsync();
            var payments = await _context.Payments.ToListAsync();

            ViewBag.TotalDoctors = doctors.Count;
            ViewBag.TotalPatients = patients.Count;
            ViewBag.TotalAppointments = appointments.Count;
            ViewBag.TotalPayments = payments.Sum(p => p.Amount);

            return View();
        }

        // ---------------------------
        // View all doctors
        // ---------------------------
        public async Task<IActionResult> Doctors()
        {
            var doctors = await _context.Doctors
                .Include(d => d.User)
                .ToListAsync();
            return View(doctors);
        }

        // ---------------------------
        // Pending Doctors
        // ---------------------------
        public async Task<IActionResult> PendingDoctors()
        {
            var pendingDoctors = await _context.Doctors
                .Include(d => d.User)
                .Where(d => !d.IsApproved)
                .ToListAsync();

            return View(pendingDoctors);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveDoctor(int id)
        {
            var doctor = await _context.Doctors.FindAsync(id);
            if (doctor == null)
            {
                TempData["Message"] = "Doctor not found.";
                return RedirectToAction(nameof(PendingDoctors));
            }

            doctor.IsApproved = true;
            _context.Update(doctor);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Doctor approved successfully.";
            return RedirectToAction(nameof(PendingDoctors));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectDoctor(int id)
        {
            var doctor = await _context.Doctors.FindAsync(id);
            if (doctor == null)
            {
                TempData["Message"] = "Doctor not found.";
                return RedirectToAction(nameof(PendingDoctors));
            }

            _context.Doctors.Remove(doctor);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Doctor rejected successfully.";
            return RedirectToAction(nameof(PendingDoctors));
        }


        // ---------------------------
        // Pending Doctor Schedules
        // ---------------------------
        public async Task<IActionResult> PendingSchedules()
        {
            var pendingSchedules = await _context.DoctorSchedules
                .Include(s => s.Doctor)
                    .ThenInclude(d => d.User)
                .Where(s => !s.IsApproved)
                .OrderByDescending(s => s.Date)
                .ToListAsync();

            return View(pendingSchedules);
        }

        // ---------------------------
        // Fixed: Individual Approve Schedule
        // ---------------------------
        [HttpPost]
        public async Task<IActionResult> ApproveSchedule([FromBody] ScheduleActionRequest request)
        {
            if (request == null || request.ScheduleId <= 0)
                return Json(new { success = false, message = "Invalid schedule." });

            var schedule = await _context.DoctorSchedules.FindAsync(request.ScheduleId);
            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            schedule.IsApproved = true;
            _context.Update(schedule);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Schedule approved successfully." });
        }

        [HttpPost]
        public async Task<IActionResult> RejectSchedule([FromBody] ScheduleActionRequest request)
        {
            if (request == null || request.ScheduleId <= 0)
                return Json(new { success = false, message = "Invalid schedule." });

            var schedule = await _context.DoctorSchedules.FindAsync(request.ScheduleId);
            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            _context.DoctorSchedules.Remove(schedule);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Schedule rejected successfully." });
        }


        // ---------------------------
        // Approve All & Reject All Schedules
        // ---------------------------
        [HttpPost]
        public async Task<IActionResult> ApproveAllSchedules([FromBody] DoctorActionRequest request)
        {
            if (request == null || request.DoctorId <= 0)
                return Json(new { success = false, message = "Invalid request." });

            var schedules = await _context.DoctorSchedules
                .Where(s => s.DoctorId == request.DoctorId && !s.IsApproved)
                .ToListAsync();

            if (!schedules.Any())
                return Json(new { success = false, message = "No pending schedules found for this doctor." });

            foreach (var s in schedules)
                s.IsApproved = true;

            _context.DoctorSchedules.UpdateRange(schedules);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "All schedules approved successfully." });
        }

        [HttpPost]
        public async Task<IActionResult> RejectAllSchedules([FromBody] DoctorActionRequest request)
        {
            if (request == null || request.DoctorId <= 0)
                return Json(new { success = false, message = "Invalid request." });

            var schedules = await _context.DoctorSchedules
                .Where(s => s.DoctorId == request.DoctorId && !s.IsApproved)
                .ToListAsync();

            if (!schedules.Any())
                return Json(new { success = false, message = "No pending schedules found for this doctor." });

            _context.DoctorSchedules.RemoveRange(schedules);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "All pending schedules rejected successfully." });
        }

        // ---------------------------
        // Edit Schedule
        // ---------------------------
        [HttpPost]
        public async Task<IActionResult> EditSchedule([FromBody] DoctorSchedule updated)
        {
            if (updated == null)
                return Json(new { success = false, message = "Invalid data." });

            var schedule = await _context.DoctorSchedules.FindAsync(updated.ScheduleId);
            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            schedule.Date = updated.Date;
            schedule.StartTime = updated.StartTime;
            schedule.EndTime = updated.EndTime;

            _context.Update(schedule);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Schedule updated successfully." });
        }

        // ---------------------------
        // Delete Schedule
        // ---------------------------
        [HttpPost]
        public async Task<IActionResult> DeleteSchedule(int id)
        {
            var schedule = await _context.DoctorSchedules.FindAsync(id);
            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            _context.DoctorSchedules.Remove(schedule);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Schedule deleted successfully." });
        }

        // ---------------------------
        // Appointments
        // ---------------------------
        public async Task<IActionResult> Appointments()
        {
            var appointments = await _context.Appointments
                .Include(a => a.Doctor).ThenInclude(d => d.User)
                .Include(a => a.Patient).ThenInclude(p => p.User)
                .OrderByDescending(a => a.ScheduledAt)
                .ToListAsync();

            return View(appointments);
        }

        // ---------------------------
        // Payments
        // ---------------------------
        public async Task<IActionResult> Payments()
        {
            var payments = await _context.Payments
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                .ToListAsync();

            return View(payments);
        }

        // ---------------------------
        // Reports
        // ---------------------------
        public async Task<IActionResult> Report()
        {
            // Total counts
            var totalDoctors = await _context.Doctors.CountAsync();
            var totalPatients = await _context.Patients.CountAsync();
            var totalAppointments = await _context.Appointments.CountAsync();
            var completedAppointments = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.Completed);

            // Pending counts
            var pendingPayments = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.PendingPayment);
            var pendingDoctorApprovals = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.AwaitingDoctorApproval);

            // Last 6 months appointments for chart
            var monthlyData = await _context.Appointments
                .GroupBy(a => new { a.ScheduledAt.Year, a.ScheduledAt.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Count = g.Count()
                })
                .OrderByDescending(g => g.Year)
                .ThenByDescending(g => g.Month)
                .Take(6)
                .ToListAsync();

            var monthlyAppointments = monthlyData
                .Select(g => new
                {
                    Month = new DateTime(g.Year, g.Month, 1).ToString("MMM yyyy"),
                    g.Count
                })
                .Reverse() // So chart shows oldest → newest
                .ToList();

            // Pass to view
            ViewBag.TotalDoctors = totalDoctors;
            ViewBag.TotalPatients = totalPatients;
            ViewBag.TotalAppointments = totalAppointments;
            ViewBag.CompletedAppointments = completedAppointments;
            ViewBag.PendingPayments = pendingPayments;
            ViewBag.PendingDoctorApprovals = pendingDoctorApprovals;
            ViewBag.MonthlyAppointments = monthlyAppointments;

            return View();
        }



        // ---------------------------
        // Delete User
        // ---------------------------
        [HttpPost]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "User deleted successfully." });
        }
    }
}
