using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;
using Telemed.ViewModels;
using Telemed.Services;


[Authorize]
public class AppointmentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailSenderExtended _emailSender;

    public AppointmentsController(
        ApplicationDbContext context,
        IEmailSenderExtended emailSender)
    {
        _context = context;
        _emailSender = emailSender;
    }


    // ---------- TIMEZONE HELPERS (Dhaka-aware) ----------
    private TimeZoneInfo GetDhakaTimeZone()
    {
        // Try common TZ IDs for Linux and Windows. Fallback to fixed +06:00.
        try
        {
            // Linux/macOS typical ID
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");
        }
        catch
        {
            try
            {
                // Windows typical ID
                return TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time");
            }
            catch
            {
                // fallback: create fixed offset +06:00
                return TimeZoneInfo.CreateCustomTimeZone("UTC+06", TimeSpan.FromHours(6), "UTC+06", "UTC+06");
            }
        }
    }

    private DateTime ClipToMinute(DateTime dt)
    {
        // remove seconds/milliseconds; preserve Kind if specified
        var kind = dt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Unspecified : dt.Kind;
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, kind);
    }

    // Convert a DateTime that represents a local Dhaka time into UTC (for storing)
    private DateTime DhakaToUtc(DateTime dhakaLocal)
    {
        var tz = GetDhakaTimeZone();

        // If input is unspecified or local, treat it as Dhaka local
        var local = DateTime.SpecifyKind(ClipToMinute(dhakaLocal), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    // Convert UTC from DB to Dhaka local time for display/comparison
    private DateTime UtcToDhaka(DateTime utc)
    {
        var tz = GetDhakaTimeZone();
        var utcKind = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var dhaka = TimeZoneInfo.ConvertTimeFromUtc(utcKind, tz);
        // make it unspecified kind (we treat it as a display local)
        return DateTime.SpecifyKind(ClipToMinute(dhaka), DateTimeKind.Unspecified);
    }

    // Canonical slot key in Dhaka local minute resolution: "yyyy-MM-dd HH:mm"
    private string DhakaSlotKeyFromUtc(DateTime utc)
    {
        var dhaka = UtcToDhaka(utc);
        return dhaka.ToString("yyyy-MM-dd HH:mm");
    }

    // Canonical slot key from a Dhaka local DateTime
    private string DhakaSlotKeyFromLocal(DateTime dhakaLocal)
    {
        var clipped = ClipToMinute(dhakaLocal);
        return clipped.ToString("yyyy-MM-dd HH:mm");
    }

    // ---------- PATIENT: CREATE ----------
    [Authorize(Roles = "Patient")]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var doctors = await _context.Doctors
            .Include(d => d.User)
            .Where(d => d.IsApproved)
            .Select(d => new { d.DoctorId, FullName = d.User.FullName, d.ConsultationFee })
            .ToListAsync();

        ViewBag.Doctors = doctors;

        return View(new AppointmentCreateViewModel
        {
            ScheduledAt = UtcToDhaka(DateTime.UtcNow).AddDays(1)
        });
    }

    [Authorize(Roles = "Patient")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AppointmentCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var doctors = await _context.Doctors
                .Include(d => d.User)
                .Where(d => d.IsApproved)
                .Select(d => new { d.DoctorId, FullName = d.User.FullName, d.ConsultationFee })
                .ToListAsync();

            ViewBag.Doctors = doctors;
            return View(model);
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim))
            return Json(new { success = false, message = "Invalid session. Please log in again." });

        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userIdClaim);
        if (patient == null)
            return Json(new { success = false, message = "Patient not found." });

        var requestedDhaka = ClipToMinute(model.ScheduledAt);

        var schedule = await _context.DoctorSchedules
            .FirstOrDefaultAsync(s =>
                s.DoctorId == model.DoctorId &&
                s.Date == requestedDhaka.Date);

        if (schedule == null)
            return Json(new { success = false, message = "Doctor has no schedule on this date." });

        if (schedule.EndTime <= schedule.StartTime)
            return Json(new { success = false, message = "Invalid schedule." });

        var totalMinutes = (schedule.EndTime - schedule.StartTime).TotalMinutes;
        if (totalMinutes < 1)
            return Json(new { success = false, message = "Doctor's available time is too short." });

        var slotMinutes = Math.Max(1, totalMinutes / schedule.MaxPatientsPerDay);
        var slotDuration = TimeSpan.FromMinutes(slotMinutes);

        var scheduleStartDhaka = ClipToMinute(schedule.Date.Add(schedule.StartTime));
        var scheduleEndDhaka = ClipToMinute(schedule.Date.Add(schedule.EndTime));

        if (requestedDhaka < scheduleStartDhaka || requestedDhaka >= scheduleEndDhaka)
            return Json(new { success = false, message = "Time is outside the schedule." });

        var minutesFromStart = (requestedDhaka - scheduleStartDhaka).TotalMinutes;
        var slotIndex = (int)Math.Floor(minutesFromStart / slotDuration.TotalMinutes);
        if (slotIndex < 0) slotIndex = 0;

        var nearestSlotDhaka = ClipToMinute(scheduleStartDhaka.AddMinutes(slotIndex * slotDuration.TotalMinutes));
        var nearestKey = DhakaSlotKeyFromLocal(nearestSlotDhaka);

        var dayLocal = schedule.Date.Date;
        var dayStartUtc = DhakaToUtc(dayLocal);
        var dayEndUtc = DhakaToUtc(dayLocal.AddDays(1));

        // 🔥 ignore cancelled/refunded/rejected
        var bookedUtc = await _context.Appointments
            .Where(a => a.DoctorId == model.DoctorId
                        && a.ScheduledAt >= dayStartUtc
                        && a.ScheduledAt < dayEndUtc
                        && a.Status != AppointmentStatus.CancelledByPatient
                        && a.Status != AppointmentStatus.CancelledByDoctor
                        && a.Status != AppointmentStatus.RefundCompleted
                        && a.Status != AppointmentStatus.Rejected)
            .Select(a => a.ScheduledAt)
            .ToListAsync();

        var bookedKeys = bookedUtc.Select(DhakaSlotKeyFromUtc).ToHashSet();

        if (bookedKeys.Contains(nearestKey))
            return Json(new { success = false, message = "This slot is already booked." });

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == model.DoctorId);
        if (doctor == null)
            return Json(new { success = false, message = "Doctor not found." });

        var nearestUtc = DhakaToUtc(nearestSlotDhaka);

        var appointment = new Appointment
        {
            DoctorId = model.DoctorId,
            PatientId = patient.PatientId,
            ScheduledAt = nearestUtc,
            PatientNote = model.PatientNote ?? "",
            DoctorNote = "",
            Status = AppointmentStatus.PendingPayment,
            ScheduleId = schedule.ScheduleId
        };

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        var payment = new Payment
        {
            AppointmentId = appointment.AppointmentId,
            Amount = doctor.ConsultationFee,
            Status = PaymentStatus.Pending,
            PaymentDate = null
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        return RedirectToAction("StartPayment", "Payments", new { appointmentId = appointment.AppointmentId });
    }

    [Authorize(Roles = "Patient")]
    [HttpGet]
    public async Task<IActionResult> PreviewRefund(int id)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.AppointmentId == id);

        if (appointment == null)
            return Json(new { success = false });

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == id);

        if (payment == null || payment.Status != PaymentStatus.Paid)
            return Json(new { success = true, refund = 0 });

        var nowUtc = DateTime.UtcNow;
        var diff = appointment.ScheduledAt - nowUtc;

        decimal refund = 0;

        if (diff.TotalHours > 24)
            refund = payment.Amount;
        else if (diff.TotalHours > 12)
            refund = payment.Amount * 0.5m;
        else
            refund = 0;

        return Json(new { success = true, refund });
    }


    /// ---------- CANCEL ----------
    [Authorize(Roles = "Patient")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var appointment = await _context.Appointments
            .Include(a => a.Patient)
                .ThenInclude(p => p.User)
            .Include(a => a.Doctor)
                .ThenInclude(d => d.User)
            .FirstOrDefaultAsync(a => a.AppointmentId == id);

        if (appointment == null)
            return NotFound();

        if (appointment.Patient.UserId != userId)
            return Forbid();

        if (appointment.Status == AppointmentStatus.Completed)
        {
            TempData["Error"] = "You cannot cancel a completed appointment.";
            return RedirectToAction("Index");
        }

        var nowUtc = DateTime.UtcNow;
        var diff = appointment.ScheduledAt - nowUtc;

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == appointment.AppointmentId);

        decimal refundAmount = 0m;
        RefundStatus finalRefundStatus = RefundStatus.None;

        //
        // REFUND LOGIC
        //
        if (payment != null && payment.Status == PaymentStatus.Paid)
        {
            if (payment.RefundStatus == RefundStatus.Full ||
                payment.RefundStatus == RefundStatus.Partial)
            {
                TempData["Error"] = "Refund already processed for this appointment.";
                appointment.Status = AppointmentStatus.CancelledByPatient;
                await _context.SaveChangesAsync();
                return RedirectToAction("Index");
            }

            if (diff.TotalHours > 24)
                refundAmount = payment.Amount;
            else if (diff.TotalHours > 12)
                refundAmount = payment.Amount * 0.5m;
            else
                refundAmount = 0;

            if (refundAmount > 0 && !string.IsNullOrWhiteSpace(payment.StripePaymentIntentId))
            {
                try
                {
                    var refundService = new Stripe.RefundService();

                    var refund = await refundService.CreateAsync(
                        new Stripe.RefundCreateOptions
                        {
                            PaymentIntent = payment.StripePaymentIntentId,
                            Amount = (long)Math.Round(refundAmount * 100m)
                        });

                    payment.RefundAmount = refundAmount;
                    payment.RefundDate = DateTime.UtcNow;
                    payment.StripeRefundId = refund.Id;

                    finalRefundStatus =
                        refundAmount == payment.Amount
                            ? RefundStatus.Full
                            : RefundStatus.Partial;

                    payment.RefundStatus = finalRefundStatus;
                    appointment.Status = AppointmentStatus.RefundCompleted;
                }
                catch (Exception ex)
                {
                    payment.RefundStatus = RefundStatus.Failed;
                    appointment.Status = AppointmentStatus.CancelledByPatient;
                    TempData["Error"] = "Refund failed: " + ex.Message;
                }
            }
            else
            {
                appointment.Status = AppointmentStatus.CancelledByPatient;
            }
        }
        else
        {
            appointment.Status = AppointmentStatus.CancelledByPatient;
        }

        await _context.SaveChangesAsync();

        //
        // SEND EMAIL
        //
        try
        {
            if (appointment.Patient?.User == null)
            {
                TempData["Warning"] = "Cancellation successful but email notification could not be sent (user data missing).";
            }
            else
            {
                var patientEmail = appointment.Patient.User.Email;
                var patientName = appointment.Patient.User.FullName ?? "Patient";

                if (string.IsNullOrWhiteSpace(patientEmail))
                {
                    TempData["Warning"] = "Cancellation successful but email notification could not be sent (no email address).";
                }
                else
                {
                    string subject;
                    string html;

                    if (finalRefundStatus == RefundStatus.Full)
                    {
                        subject = "TeleMed — Full refund processed";
                        html = $@"
<h2>Your refund is complete</h2>

<p>Hi {patientName},</p>

<p>Your appointment (ID: <strong>{appointment.AppointmentId}</strong>) was cancelled.</p>

<p><strong>Refund:</strong> ৳{payment?.RefundAmount:N2}</p>

<p>The refund will appear in your account within 5-10 business days.</p>

<p>Regards,<br/>TeleMed Support</p>";
                    }
                    else if (finalRefundStatus == RefundStatus.Partial)
                    {
                        subject = "TeleMed — Partial refund processed";
                        html = $@"
<h2>Your partial refund is complete</h2>

<p>Hi {patientName},</p>

<p>Your appointment (ID: <strong>{appointment.AppointmentId}</strong>) was cancelled.</p>

<p><strong>Refund:</strong> ৳{payment?.RefundAmount:N2}</p>

<p>The refund will appear in your account within 5-10 business days.</p>

<p>Regards,<br/>TeleMed Support</p>";
                    }
                    else
                    {
                        subject = "TeleMed — Appointment cancelled";
                        html = $@"
<h2>Appointment cancelled</h2>

<p>Hi {patientName},</p>

<p>Your appointment (ID: <strong>{appointment.AppointmentId}</strong>) was cancelled.</p>

<p><strong>No refund issued</strong> (cancelled less than 12 hours before appointment).</p>

<p>Regards,<br/>TeleMed Support</p>";
                    }

                    await _emailSender.SendEmailWithAttachmentAsync(
                        patientEmail,
                        subject,
                        html,
                        null,
                        null
                    );
                }
            }
        }
        catch (Exception ex)
        {
            TempData["Warning"] = "Cancellation successful but email notification failed. Please check your email settings.";
        }

        TempData["Success"] = refundAmount > 0
            ? $"Appointment cancelled. Refund processed: ৳{refundAmount:N2}"
            : "Appointment cancelled.";

        return RedirectToAction("Index");
    }






    [Authorize(Roles = "Patient")]
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.UserId == User.Identity.Name || p.User.Email == User.Identity.Name);

        if (patient == null) return NotFound();

        var appointments = await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Include(a => a.Schedule)
            .Where(a => a.PatientId == patient.PatientId)
            .OrderByDescending(a => a.ScheduledAt)
            .ToListAsync();

        var missingScheduleIds = appointments
            .Where(a => a.Schedule == null && a.ScheduleId.HasValue)
            .Select(a => a.ScheduleId!.Value)
            .Distinct()
            .ToList();

        var schedulesById = new List<DoctorSchedule>();
        if (missingScheduleIds.Any())
        {
            schedulesById = await _context.DoctorSchedules
                .Where(s => missingScheduleIds.Contains(s.ScheduleId))
                .ToListAsync();
        }

        var appointmentsMissingScheduleButHaveDoctorDate = appointments
            .Where(a => a.Schedule == null && !a.ScheduleId.HasValue)
            .ToList();

        var schedulesFoundByDoctorDate = new List<DoctorSchedule>();
        if (appointmentsMissingScheduleButHaveDoctorDate.Any())
        {
            var doctorDatePairs = appointmentsMissingScheduleButHaveDoctorDate
                .Select(a => new { a.DoctorId, Date = a.ScheduledAt.ToUniversalTime().Date })
                .Distinct()
                .ToList();

            foreach (var pair in doctorDatePairs)
            {
                var s = await _context.DoctorSchedules
                    .Where(x => x.DoctorId == pair.DoctorId && x.Date == pair.Date) // no IsApproved
                    .OrderByDescending(x => x.ScheduleId)
                    .FirstOrDefaultAsync();

                if (s != null) schedulesFoundByDoctorDate.Add(s);
            }
        }

        foreach (var a in appointments)
        {
            if (a.Schedule == null)
            {
                if (a.ScheduleId.HasValue)
                {
                    a.Schedule = schedulesById.FirstOrDefault(s => s.ScheduleId == a.ScheduleId.Value);
                }

                if (a.Schedule == null)
                {
                    var targetDate = a.ScheduledAt.ToUniversalTime().Date;
                    a.Schedule = schedulesFoundByDoctorDate
                        .FirstOrDefault(s => s.DoctorId == a.DoctorId && s.Date == targetDate);
                }
            }

            if (a.Schedule == null && a.ScheduleId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"[Index] Appointment {a.AppointmentId} has ScheduleId={a.ScheduleId} but schedule nav is null and no schedule found.");
            }
        }

        var appointmentIds = appointments.Select(a => a.AppointmentId).ToList();
        var payments = await _context.Payments
            .Where(p => appointmentIds.Contains(p.AppointmentId))
            .ToListAsync();

        var feedbackIds = await _context.Feedbacks
            .Where(f => f.PatientId == patient.PatientId)
            .Select(f => f.AppointmentId)
            .ToListAsync();

        var model = appointments.Select(a => new AppointmentWithFeedbackStatus
        {
            Appointment = a,
            HasFeedback = feedbackIds.Contains(a.AppointmentId),
            Payment = payments.FirstOrDefault(p => p.AppointmentId == a.AppointmentId)
        });

        return View(model);
    }

    // ---------- DOCTOR ACTIONS ----------
    [Authorize(Roles = "Doctor")]
    [HttpGet]
    public async Task<IActionResult> DoctorIndex()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim))
            return RedirectToAction("Login", "Account");

        var doctor = await _context.Doctors
            .Include(d => d.Appointments)
                .ThenInclude(a => a.Patient)
                    .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(d => d.UserId == userIdClaim);

        if (doctor == null)
            return RedirectToAction("Login", "Account");

        var appointments = doctor.Appointments
            .OrderBy(a => a.ScheduledAt)
            .ToList();

        return View("DoctorIndex", appointments);
    }

    [Authorize(Roles = "Doctor")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Doctor)
            .Include(a => a.Schedule)
            .FirstOrDefaultAsync(a => a.AppointmentId == id);

        if (appointment == null)
            return NotFound();

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (appointment.Doctor.UserId != userId)
            return Forbid();

        var schedule = await _context.DoctorSchedules
            .FirstOrDefaultAsync(s =>
                s.DoctorId == appointment.DoctorId &&
                s.Date == appointment.ScheduledAt.ToUniversalTime().Date); // no IsApproved

        if (schedule != null)
            appointment.ScheduleId = schedule.ScheduleId;

        appointment.Status = AppointmentStatus.Approved;
        await _context.SaveChangesAsync();

        return RedirectToAction("DoctorIndex");
    }

    [Authorize(Roles = "Doctor")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.AppointmentId == id);

        if (appointment == null)
            return NotFound();

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (appointment.Doctor.UserId != userId)
            return Forbid();

        appointment.Status = AppointmentStatus.Rejected;
        await _context.SaveChangesAsync();

        return RedirectToAction("DoctorIndex");
    }

    // ---------- COMMON ACTIONS ----------
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Doctor)
                .ThenInclude(d => d.User)
            .Include(a => a.Patient)
                .ThenInclude(p => p.User)
            .Include(a => a.Schedule)
            .FirstOrDefaultAsync(a => a.AppointmentId == id);

        if (appointment == null)
            return NotFound();

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Forbid();

        var isPatient = User.IsInRole("Patient") && appointment.Patient.UserId == userId;
        var isDoctor = User.IsInRole("Doctor") && appointment.Doctor.UserId == userId;
        var isAdmin = User.IsInRole("Admin");

        if (!isPatient && !isDoctor && !isAdmin)
            return Forbid();

        if (appointment.Schedule == null && appointment.ScheduleId.HasValue)
        {
            appointment.Schedule = await _context.DoctorSchedules
                .FirstOrDefaultAsync(s => s.ScheduleId == appointment.ScheduleId.Value);
        }

        if (appointment.ScheduleId == null)
        {
            var schedule = await _context.DoctorSchedules
                .FirstOrDefaultAsync(s =>
                    s.DoctorId == appointment.DoctorId &&
                    s.Date == appointment.ScheduledAt.ToUniversalTime().Date); // no IsApproved

            if (schedule != null)
            {
                appointment.ScheduleId = schedule.ScheduleId;
                appointment.Schedule = schedule;
                await _context.SaveChangesAsync();
            }
        }

        return View(appointment);
    }

    [Authorize(Roles = "Patient")]
    [HttpGet]
    public async Task<IActionResult> JoinVideo(int appointmentId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim)) return Forbid();

        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Schedule)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

        if (appointment == null) return NotFound();

        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userIdClaim);
        if (patient == null || appointment.PatientId != patient.PatientId) return Forbid();

        var payment = await _context.Payments.FirstOrDefaultAsync(p => p.AppointmentId == appointmentId);
        if (payment == null || payment.Status != PaymentStatus.Paid) return Forbid();

        TimeSpan slotDuration;
        if (appointment.Schedule != null)
        {
            var schedule = appointment.Schedule;
            var totalMinutes = (schedule.EndTime - schedule.StartTime).TotalMinutes;
            if (totalMinutes <= 0 || schedule.MaxPatientsPerDay <= 0)
                slotDuration = TimeSpan.FromMinutes(30);
            else
                slotDuration = TimeSpan.FromMinutes(Math.Max(5, totalMinutes / schedule.MaxPatientsPerDay));
        }
        else
        {
            var schedule = await _context.DoctorSchedules
                .FirstOrDefaultAsync(s => s.DoctorId == appointment.DoctorId && s.Date == appointment.ScheduledAt.ToUniversalTime().Date); // no IsApproved
            if (schedule != null)
            {
                var totalMinutes = (schedule.EndTime - schedule.StartTime).TotalMinutes;
                slotDuration = totalMinutes > 0 && schedule.MaxPatientsPerDay > 0
                    ? TimeSpan.FromMinutes(Math.Max(5, totalMinutes / schedule.MaxPatientsPerDay))
                    : TimeSpan.FromMinutes(30);
            }
            else
            {
                slotDuration = TimeSpan.FromMinutes(30);
            }
        }

        var nowUtc = DateTime.UtcNow;
        var scheduledUtc = DateTime.SpecifyKind(appointment.ScheduledAt, DateTimeKind.Utc);
        var enableAt = scheduledUtc.AddMinutes(-5);
        var appointmentEnd = scheduledUtc.Add(slotDuration);

        if (nowUtc < enableAt) return Forbid();
        if (nowUtc > appointmentEnd) return Forbid();

        var link = appointment.Schedule?.VideoCallLink;
        if (string.IsNullOrWhiteSpace(link)) return NotFound();

        return Redirect(link);
    }

    // ---------- ADMIN & BOOK & GetAvailableSlots ----------
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> AdminIndex()
    {
        var appointments = await _context.Appointments
            .Include(a => a.Patient)
                .ThenInclude(p => p.User)
            .Include(a => a.Doctor)
                .ThenInclude(d => d.User)
            .OrderByDescending(a => a.ScheduledAt)
            .ToListAsync();

        return View("AdminIndex", appointments);
    }

    [Authorize(Roles = "Patient")]
    [HttpGet]
    public async Task<IActionResult> Book(int? doctorId)
    {
        var doctors = await _context.Doctors
            .Include(d => d.User)
            .Where(d => d.IsApproved)
            .Select(d => new { d.DoctorId, FullName = d.User.FullName, d.ConsultationFee })
            .ToListAsync();

        ViewBag.Doctors = doctors;

        var model = new AppointmentCreateViewModel
        {
            ScheduledAt = UtcToDhaka(DateTime.UtcNow).AddDays(1)
        };

        if (doctorId.HasValue)
        {
            model.DoctorId = doctorId.Value;
            ViewBag.SelectedDoctorId = doctorId.Value;
            ViewBag.IsDoctorLocked = true;
        }
        else
        {
            ViewBag.SelectedDoctorId = null;
            ViewBag.IsDoctorLocked = false;
        }

        return View("Create", model);
    }

    [HttpGet]
    public async Task<IActionResult> GetAvailableSlots(int doctorId, DateTime date)
    {
        var requestedDate = date.Date;

        var schedule = await _context.DoctorSchedules
            .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.Date == requestedDate);

        if (schedule == null)
            return Json(new { success = false, message = "Doctor has no schedule on this date." });

        if (schedule.EndTime <= schedule.StartTime)
            return Json(new { success = false, message = "Invalid schedule." });

        var totalMinutes = (schedule.EndTime - schedule.StartTime).TotalMinutes;
        if (totalMinutes < 1)
            return Json(new { success = false, message = "Schedule too short." });

        var slotMinutes = Math.Max(1, totalMinutes / schedule.MaxPatientsPerDay);
        var slotDuration = TimeSpan.FromMinutes(slotMinutes);

        var slotsDhaka = new List<DateTime>();
        var currentDhaka = ClipToMinute(schedule.Date.Add(schedule.StartTime));
        var endDhaka = ClipToMinute(schedule.Date.Add(schedule.EndTime));

        while (currentDhaka + slotDuration <= endDhaka)
        {
            slotsDhaka.Add(ClipToMinute(currentDhaka));
            currentDhaka = currentDhaka.Add(slotDuration);
        }

        var startUtc = DhakaToUtc(schedule.Date);
        var endUtc = DhakaToUtc(schedule.Date.AddDays(1));

        // 🔥 ignore cancelled/refunded/rejected
        var bookedUtc = await _context.Appointments
            .Where(a => a.DoctorId == doctorId
                        && a.ScheduledAt >= startUtc
                        && a.ScheduledAt < endUtc
                        && a.Status != AppointmentStatus.CancelledByPatient
                        && a.Status != AppointmentStatus.CancelledByDoctor
                        && a.Status != AppointmentStatus.RefundCompleted
                        && a.Status != AppointmentStatus.Rejected)
            .Select(a => a.ScheduledAt)
            .ToListAsync();

        var bookedKeys = bookedUtc.Select(DhakaSlotKeyFromUtc).ToHashSet();

        var availableSlots = slotsDhaka
            .Where(s => !bookedKeys.Contains(DhakaSlotKeyFromLocal(s)))
            .Select(s => s.ToString("hh:mm tt"))
            .ToList();

        if (!availableSlots.Any())
            return Json(new { success = false, message = "No available slots for this date." });

        var doctor = await _context.Doctors.FindAsync(doctorId);
        decimal fee = doctor?.ConsultationFee ?? 0;

        return Json(new { success = true, slots = availableSlots, fee });
    }
}
