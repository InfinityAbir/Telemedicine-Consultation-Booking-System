using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;
using Telemed.ViewModels;

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
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Payments()
        {
            var payments = await _context.Payments
                .AsNoTracking()
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pt => pt.User)   // 🔹 load Patient -> User
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)     // 🔹 load Doctor -> User
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            return View(payments);
        }

        // ---------------------------
        // Reports
        // ---------------------------
        public async Task<IActionResult> Report()
        {
            // ---------- BASIC COUNTS ----------
            var totalDoctors = await _context.Doctors.CountAsync();
            var totalPatients = await _context.Patients.CountAsync();
            var totalAppointments = await _context.Appointments.CountAsync();

            var completedAppointments = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.Completed);

            var pendingPayments = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.PendingPayment);

            var pendingDoctorApprovals = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.AwaitingDoctorApproval);

            // ---------- PAID PAYMENTS (SOURCE OF TRUTH FOR MONEY) ----------
            var paidPaymentsQuery = _context.Payments
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                .Where(p => p.Status == PaymentStatus.Paid);

            // ---------- TOTAL REVENUE ----------
            var totalRevenue = await paidPaymentsQuery
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            // ---------- PER-DOCTOR EARNINGS ----------
            var doctorEarnings = await paidPaymentsQuery
                .Where(p => p.Appointment != null && p.Appointment.Doctor != null)
                .GroupBy(p => new
                {
                    p.Appointment.DoctorId,
                    DoctorName = p.Appointment.Doctor.User.FullName,
                    p.Appointment.Doctor.Specialization
                })
                .Select(g => new DoctorEarningSummary
                {
                    DoctorId = g.Key.DoctorId,
                    DoctorName = g.Key.DoctorName,
                    Specialization = g.Key.Specialization,
                    TotalEarned = g.Sum(x => x.Amount),
                    TotalPaidAppointments = g.Count()
                })
                .OrderByDescending(d => d.TotalEarned)
                .ToListAsync();

            // ---------- DOCTOR AVAILABILITY (only next 7 days from today) ----------
            var startDate = DateTime.Today;
            var endDate = startDate.AddDays(7); // today + next 6 days

            var doctorAvailabilities = await _context.DoctorSchedules
                .Include(s => s.Doctor).ThenInclude(d => d.User)
                .Where(s => s.Date >= startDate && s.Date < endDate)
                .OrderBy(s => s.Date)
                .ThenBy(s => s.StartTime)
                .Select(s => new DoctorAvailabilitySummary
                {
                    ScheduleId = s.ScheduleId,
                    DoctorId = s.DoctorId,
                    DoctorName = s.Doctor.User.FullName,
                    Specialization = s.Doctor.Specialization,
                    Date = s.Date,
                    DayName = s.Date.ToString("dddd"),
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    MaxPatientsPerDay = s.MaxPatientsPerDay,
                    IsApproved = s.IsApproved
                })
                .ToListAsync();

            // ---------- LAST 6 MONTHS APPOINTMENTS (chart) ----------
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
                .Select(g => new MonthlyAppointmentSummary
                {
                    Month = new DateTime(g.Year, g.Month, 1).ToString("MMM yyyy"),
                    Count = g.Count
                })
                .Reverse()
                .ToList();

            // ---------- FILL VIEWMODEL ----------
            var model = new AdminReportViewModel
            {
                TotalDoctors = totalDoctors,
                TotalPatients = totalPatients,
                TotalAppointments = totalAppointments,
                CompletedAppointments = completedAppointments,
                PendingPayments = pendingPayments,
                PendingDoctorApprovals = pendingDoctorApprovals,

                TotalRevenue = totalRevenue,
                DoctorEarnings = doctorEarnings,

                DoctorAvailabilities = doctorAvailabilities,
                MonthlyAppointments = monthlyAppointments
            };

            return View(model);
        }




    }
}
