using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;

namespace Telemed.Controllers
{
    [Authorize]
    public class DoctorSchedulesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DoctorSchedulesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> MySchedules()
        {
            var userId = _context.Users
                .Where(u => u.UserName == User.Identity.Name)
                .Select(u => u.Id)
                .FirstOrDefault();

            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
            if (doctor == null) return NotFound("Doctor not found.");

            var schedules = await _context.DoctorSchedules
                .Where(s => s.DoctorId == doctor.DoctorId)
                .OrderByDescending(s => s.Date)
                .ToListAsync();

            return View(schedules);
        }

        [Authorize(Roles = "Doctor")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Create(DoctorScheduleMultiDayViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please fill all required fields correctly.";
                return View(model);
            }

            var userId = _context.Users
                .Where(u => u.UserName == User.Identity.Name)
                .Select(u => u.Id)
                .FirstOrDefault();

            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
            if (doctor == null)
            {
                TempData["Error"] = "Doctor profile not found.";
                return View(model);
            }

            // Use TimeSpan directly
            var startTime = model.StartTime;
            var endTime = model.EndTime;

            if (endTime <= startTime)
            {
                TempData["Error"] = "End time must be after start time.";
                return View(model);
            }

            // total available minutes per day
            var totalMinutes = (endTime - startTime).TotalMinutes;

            // guard: max patients must be positive
            if (model.MaxPatientsPerDay <= 0)
            {
                TempData["Error"] = "Max patients per day must be greater than zero.";
                return View(model);
            }

            // required minimum per patient
            const int minMinutesPerPatient = 10;

            // compute minutes per patient if evenly divided
            var minutesPerPatient = totalMinutes / model.MaxPatientsPerDay;

            if (minutesPerPatient < minMinutesPerPatient)
            {
                TempData["Error"] = $"Each patient must have at least {minMinutesPerPatient} minutes. " +
                                   $"With the selected time range ({startTime:hh\\:mm} - {endTime:hh\\:mm}) " +
                                   $"you can schedule at most {Math.Floor(totalMinutes / minMinutesPerPatient)} patients per day.";
                return View(model);
            }

            // OK: create schedule entries for each day in range
            for (var date = model.StartDate.Date; date <= model.EndDate.Date; date = date.AddDays(1))
            {
                bool exists = await _context.DoctorSchedules
                    .AnyAsync(s => s.DoctorId == doctor.DoctorId && s.Date == date);

                if (exists) continue;

                var newSchedule = new DoctorSchedule
                {
                    DoctorId = doctor.DoctorId,
                    Date = date,
                    StartTime = startTime,
                    EndTime = endTime,
                    MaxPatientsPerDay = model.MaxPatientsPerDay,
                    IsApproved = false,
                    VideoCallLink = model.VideoCallLink
                };

                _context.DoctorSchedules.Add(newSchedule);
            }

            await _context.SaveChangesAsync();
            TempData["Message"] = "Schedules created successfully and pending admin approval.";
            return RedirectToAction("MySchedules");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> EditSchedule(int ScheduleId, TimeSpan StartTime, TimeSpan EndTime, int MaxPatientsPerDay)
        {
            var schedule = await _context.DoctorSchedules.FindAsync(ScheduleId);
            if (schedule == null) return NotFound();

            if (EndTime <= StartTime)
            {
                var msg = "End time must be after start time.";
                if (IsAjaxRequest()) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("MySchedules");
            }

            if (MaxPatientsPerDay <= 0)
            {
                var msg = "Max patients per day must be greater than zero.";
                if (IsAjaxRequest()) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("MySchedules");
            }

            var totalMinutes = (EndTime - StartTime).TotalMinutes;
            const int minMinutesPerPatient = 10;
            var minutesPerPatient = totalMinutes / MaxPatientsPerDay;

            if (minutesPerPatient < minMinutesPerPatient)
            {
                var allowed = (int)Math.Floor(totalMinutes / minMinutesPerPatient);
                var msg = $"Each patient must have at least {minMinutesPerPatient} minutes. With the selected time range ({StartTime:hh\\:mm} - {EndTime:hh\\:mm}) you can schedule at most {allowed} patients.";
                if (IsAjaxRequest()) return Json(new { success = false, message = msg, allowed });
                TempData["Error"] = msg;
                return RedirectToAction("MySchedules");
            }

            schedule.StartTime = StartTime;
            schedule.EndTime = EndTime;
            schedule.MaxPatientsPerDay = MaxPatientsPerDay;
            schedule.IsApproved = false; // Re-approve after edit

            _context.Update(schedule);
            await _context.SaveChangesAsync();

            var successMsg = "Schedule updated successfully and sent for re-approval.";
            if (IsAjaxRequest()) return Json(new { success = true, message = successMsg });
            TempData["Message"] = successMsg;
            return RedirectToAction("MySchedules");
        }

        // helper
        private bool IsAjaxRequest()
        {
            if (Request == null) return false;
            return Request.Headers != null && Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var schedule = await _context.DoctorSchedules.FindAsync(id);
            if (schedule == null) return NotFound();

            // Prevent deletion if any appointment is linked
            bool hasAppointments = await _context.Appointments
                .AnyAsync(a => a.ScheduleId == schedule.ScheduleId);

            if (hasAppointments)
            {
                TempData["Error"] = "Cannot delete schedule with existing appointments.";
                return RedirectToAction("MySchedules");
            }

            _context.DoctorSchedules.Remove(schedule);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Schedule deleted successfully.";
            return RedirectToAction("MySchedules");
        }


        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Pending()
        {
            var pending = await _context.DoctorSchedules
                .Include(s => s.Doctor).ThenInclude(d => d.User)
                .Where(s => !s.IsApproved)
                .ToListAsync();

            return View(pending);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> Approve(int id)
        {
            var schedule = await _context.DoctorSchedules.FindAsync(id);
            if (schedule == null) return NotFound();

            schedule.IsApproved = true;
            _context.Update(schedule);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Schedule approved successfully.";
            return RedirectToAction(nameof(Pending));
        }
    }
}
