using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;
using Telemed.ViewModels;
using TelemedSystem.Services;

namespace Telemed.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly BackupService _backupService;

        public AdminController(ApplicationDbContext context, BackupService backupService)
        {
            _context = context;
            _backupService = backupService;
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
        // Pending Fee Approvals
        // ---------------------------
        public async Task<IActionResult> PendingApprovals()
        {
            var doctorsWithPendingFees = await _context.Doctors
                .Include(d => d.User)
                .Where(d => d.PendingConsultationFee != null)
                .ToListAsync();

            return View(doctorsWithPendingFees);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveFee(int id)
        {
            var doctor = await _context.Doctors.FindAsync(id);

            if (doctor == null || doctor.PendingConsultationFee == null)
                return NotFound();

            doctor.ConsultationFee = doctor.PendingConsultationFee.Value;
            doctor.PendingConsultationFee = null;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Consultation fee approved successfully.";
            return RedirectToAction(nameof(PendingApprovals));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectFee(int id)
        {
            var doctor = await _context.Doctors.FindAsync(id);

            if (doctor == null || doctor.PendingConsultationFee == null)
                return NotFound();

            doctor.PendingConsultationFee = null;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Consultation fee change rejected.";
            return RedirectToAction(nameof(PendingApprovals));
        }

        // ---------------------------
        // Pending Doctors (Onboarding)
        // ---------------------------
        public async Task<IActionResult> PendingDoctors()
        {
            var pendingItems = await _context.Doctors
                .Include(d => d.User)
                .Where(d =>
                    !d.IsApproved ||
                    d.PendingConsultationFee != null
                )
                .ToListAsync();

            return View(pendingItems);
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
        public async Task<IActionResult> Payments()
        {
            var payments = await _context.Payments
                .AsNoTracking()
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pt => pt.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            return View(payments);
        }

        // ---------------------------
        // Reports
        // ---------------------------
        public async Task<IActionResult> Report()
        {
            var totalDoctors = await _context.Doctors.CountAsync();
            var totalPatients = await _context.Patients.CountAsync();
            var totalAppointments = await _context.Appointments.CountAsync();

            var completedAppointments = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.Completed);

            var pendingPayments = await _context.Appointments
                .CountAsync(a => a.Status == AppointmentStatus.PendingPayment);

            var pendingDoctorApprovals = await _context.Doctors
                .CountAsync(d => !d.IsApproved);

            var pendingFeeChangeRequests = await _context.Doctors
                .CountAsync(d => d.PendingConsultationFee != null);


            var paidPaymentsQuery = _context.Payments
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                .Where(p => p.Status == PaymentStatus.Paid);

            var totalRevenue = await paidPaymentsQuery
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var doctorEarnings = await paidPaymentsQuery
                .Where(p => p.Appointment.Doctor != null)
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

            var startDate = DateTime.Today;
            var endDate = startDate.AddDays(7);

            var doctorAvailabilities = await _context.DoctorSchedules
                .Include(s => s.Doctor)
                    .ThenInclude(d => d.User)
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

            var monthlyAppointmentsRaw = await _context.Appointments
                .GroupBy(a => new
                {
                    Year = a.ScheduledAt.Year,
                    Month = a.ScheduledAt.Month
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .Take(6)
                .ToListAsync();

            var monthlyAppointments = monthlyAppointmentsRaw
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .Select(x => new MonthlyAppointmentSummary
                {
                    Month = new DateTime(x.Year, x.Month, 1).ToString("MMM yyyy"),
                    Count = x.Count
                })
                .ToList();

            var model = new AdminReportViewModel
            {
                TotalDoctors = totalDoctors,
                TotalPatients = totalPatients,
                TotalAppointments = totalAppointments,
                CompletedAppointments = completedAppointments,
                PendingPayments = pendingPayments,
                PendingDoctorApprovals = pendingDoctorApprovals,
                PendingFeeChangeRequests = pendingFeeChangeRequests,
                TotalRevenue = totalRevenue,
                DoctorEarnings = doctorEarnings,
                DoctorAvailabilities = doctorAvailabilities,
                MonthlyAppointments = monthlyAppointments
            };

            return View(model);
        }

        // ---------------------------
        // Manual Backup Trigger
        // ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunBackup()
        {
            await _backupService.RunBackupNowAsync();

            TempData["ReportMessage"] = "Database backup completed successfully.";
            return RedirectToAction(nameof(Report));
        }
    }
}
