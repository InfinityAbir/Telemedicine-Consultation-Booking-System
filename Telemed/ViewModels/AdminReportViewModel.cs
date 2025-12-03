using System;
using System.Collections.Generic;
using Telemed.Models;

namespace Telemed.ViewModels
{
    public class MonthlyAppointmentSummary
    {
        public string Month { get; set; } = "";
        public int Count { get; set; }
    }

    public class DoctorEarningSummary
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = "";
        public string? Specialization { get; set; }
        public decimal TotalEarned { get; set; }
        public int TotalPaidAppointments { get; set; }
    }

    public class DoctorAvailabilitySummary
    {
        public int ScheduleId { get; set; }
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = "";
        public string? Specialization { get; set; }

        public DateTime Date { get; set; }
        public string DayName { get; set; } = "";

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public int MaxPatientsPerDay { get; set; }
        public bool IsApproved { get; set; }
    }

    public class AdminReportViewModel
    {
        // High-level counters
        public int TotalDoctors { get; set; }
        public int TotalPatients { get; set; }
        public int TotalAppointments { get; set; }
        public int CompletedAppointments { get; set; }
        public int PendingPayments { get; set; }
        public int PendingDoctorApprovals { get; set; }

        // Money
        public decimal TotalRevenue { get; set; }
        public List<DoctorEarningSummary> DoctorEarnings { get; set; } = new();

        // Doctor availability (from DoctorSchedule)
        public List<DoctorAvailabilitySummary> DoctorAvailabilities { get; set; } = new();

        // Chart
        public List<MonthlyAppointmentSummary> MonthlyAppointments { get; set; } = new();
    }
}
