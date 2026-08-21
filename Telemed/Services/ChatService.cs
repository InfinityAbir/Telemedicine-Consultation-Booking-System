using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Collections.Generic;
using Telemed.Models;
using Microsoft.EntityFrameworkCore;


public class ChatService
{
    private readonly IConfiguration _config;
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _http;

    // keyword -> specialization match list
    private readonly Dictionary<string, string> _specialtyMap = new()
{
    // Gynecology / Women's health
    { "pregnancy", "Gyne" },
    { "pregnant", "Gyne" },
    { "women", "Gyne" },
    { "period", "Gyne" },
    { "missed period", "Gyne" },
    { "late period", "Gyne" },
    { "menstruation", "Gyne" },
    { "irregular period", "Gyne" },
    { "spotting", "Gyne" },
    { "vaginal bleeding", "Gyne" },
    { "pcos", "Gyne" },
    { "fertility", "Gyne" },

    // Cardiology
    { "heart", "Cardio" },
    { "chest pain", "Cardio" },
    { "shortness of breath", "Cardio" },
    { "palpitations", "Cardio" },
    { "high blood pressure", "Cardio" },
    { "bp", "Cardio" },

    // Endocrine / Diabetes
    { "sugar", "Endocrine" },
    { "diabetes", "Endocrine" },
    { "thyroid", "Endocrine" },
    { "hormone", "Endocrine" },

    // Pediatrics (kids)
    { "child", "Pediatric" },
    { "baby", "Pediatric" },
    { "kids", "Pediatric" },
    { "fever child", "Pediatric" },
    { "vaccination", "Pediatric" },
    { "cough child", "Pediatric" },

    // Dermatology
    { "skin", "Derma" },
    { "rash", "Derma" },
    { "itching", "Derma" },
    { "acne", "Derma" },
    { "hair fall", "Derma" },
    { "eczema", "Derma" },

    // Neurology
    { "brain", "Neuro" },
    { "headache", "Neuro" },
    { "migraine", "Neuro" },
    { "dizzy", "Neuro" },
    { "numbness", "Neuro" },
    { "seizure", "Neuro" },

    // Psychiatry
    { "mental", "Psych" },
    { "depression", "Psych" },
    { "anxiety", "Psych" },
    { "stress", "Psych" },
    { "panic", "Psych" },
    { "insomnia", "Psych" },

    // Gastro
    { "stomach", "Gastro" },
    { "gastric", "Gastro" },
    { "abdominal pain", "Gastro" },
    { "vomiting", "Gastro" },
    { "diarrhea", "Gastro" },
    { "constipation", "Gastro" },

    // Eye
    { "eye", "Ophthal" },
    { "vision", "Ophthal" },
    { "blurred vision", "Ophthal" },
    { "red eye", "Ophthal" },
    { "eye pain", "Ophthal" },
    { "itchy eyes", "Ophthal" }
};


    public ChatService(IConfiguration config, ApplicationDbContext db)
    {
        _config = config;
        _db = db;
        _http = new HttpClient();
    }

    public async Task<string> GetReply(string userMessage)
    {
        // 1) Emergency filter
        if (IsEmergency(userMessage))
        {
            return "This sounds serious. I can't give medical advice. Please contact a licensed doctor or emergency services immediately.";
        }

        // 2) Try suggesting doctors FROM DATABASE first
        var doctorSuggestion = TrySuggestDoctor(userMessage);
        if (doctorSuggestion != null)
            return doctorSuggestion;

        // 3) Otherwise fallback to AI
        var systemPrompt =
            "You are the assistant for a Telemedicine Booking System. " +
            "Your main job is to help users understand how to use the platform.\n\n" +

            "You can answer questions about:\n" +
            "- registration\n" +
            "- login\n" +
            "- booking appointments\n" +
            "- payments and invoices\n" +
            "- prescriptions\n" +
            "- profile/account settings\n\n" +

            "If the user is NOT logged in and tries to book:\n" +
            "Tell them they must login first.\n\n" +

            "Booking flow after login:\n" +
            "- choose doctor\n" +
            "- select date\n" +
            "- choose available time slot\n" +
            "- confirm appointment\n" +
            "- complete payment\n" +
            "- appointment confirmed and invoice emailed.\n\n" +

            "SAFETY RULES:\n" +
            "- You are NOT a doctor.\n" +
            "- Do NOT diagnose.\n" +
            "- Do NOT recommend medicines.\n" +
            "- If user asks for treatment, refuse politely and suggest booking an appointment.\n\n" +

            "IF YOU DON'T KNOW:\n" +
            "Do not guess. Say you don't know, then guide them back to platform features.\n\n" +

            "Always answer simply, clearly, and in a friendly tone.";

        var apiKey = _config["AI:GroqKey"];

        var payload = new
        {
            model = "openai/gpt-oss-120b",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            }
        };

        var json = JsonSerializer.Serialize(payload);

        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://api.groq.com/openai/v1/chat/completions"),
            Headers =
            {
                { "Authorization", $"Bearer {apiKey}" }
            },
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(text);

        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var msg = error.GetProperty("message").GetString();
            return "AI connection failed: " + msg;
        }

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content;
    }

    private readonly Dictionary<string, string> _introMap = new()
{
    { "Gyne", "Missing periods, pregnancy concerns, or women’s health issues are usually handled by gynecology specialists." },
    { "Cardio", "Chest pain, shortness of breath, or heart-related worries are usually checked by heart specialists." },
    { "Endocrine", "Sugar, thyroid, and hormone issues are handled by endocrine specialists." },
    { "Pediatric", "Child, baby, and kids’ health is best handled by children’s doctors." },
    { "Derma", "Skin problems like rashes, itching, acne, or hair fall are handled by skin specialists." },
    { "Neuro", "Headaches, dizziness, seizures, or brain-related issues usually go to neurology." },
    { "Psych", "Mental health, stress, anxiety, or depression are handled by psychiatry." },
    { "Gastro", "Stomach pain, gastric, acidity, or digestive problems go to gastro doctors." },
    { "Ophthal", "Blurry vision, eye pain, redness, or injury are handled by eye specialists." }
};


    private bool IsEmergency(string text)
    {
        text = text.ToLower();

        // If user is asking "which doctor" or "who should I see"
        // we DO NOT trigger emergency, even if symptoms appear.
        if (text.Contains("which doctor") || text.Contains("who should i see"))
            return false;

        string[] danger =
        {
        "i have chest pain",
        "having chest pain",
        "severe chest pain",
        "heart attack",
        "severe bleeding",
        "suicidal",
        "overdose",
        "can't breathe",
        "unconscious"
    };

        return danger.Any(text.Contains);
    }


    private string TrySuggestDoctor(string userMessage)
    {
        var lower = userMessage.ToLower();

        foreach (var pair in _specialtyMap)
        {
            if (lower.Contains(pair.Key))
            {
                var specialty = pair.Value;

                var doctors = _db.Doctors
                    .Include(d => d.User)
                    .Where(d =>
                        d.IsApproved &&
                        d.Specialization.ToLower().Contains(specialty.ToLower()))
                    .Select(d => new
                    {
                        d.FullName,
                        d.Specialization,
                        d.Qualification,
                        d.ConsultationFee
                    })
                    .OrderBy(x => Guid.NewGuid())      // randomize results
                    .Take(3)                           // show up to 3
                    .ToList();

                if (!doctors.Any())
                {
                    return $"I understand your concern, but right now we don’t have any approved doctors in this category. Please check again later.";
                }

                // specialty intro
                var intro = _introMap.ContainsKey(specialty)
                    ? _introMap[specialty]
                    : $"This seems related to {specialty}.";

                var msg =
    $"{intro}\n\n" +
    $"**Recommended specialists:**\n\n";

                foreach (var d in doctors)
                {
                    var qualificationText = string.IsNullOrWhiteSpace(d.Qualification)
                        ? "Not provided"
                        : d.Qualification;

                    var feeText = d.ConsultationFee > 0
                        ? $"{d.ConsultationFee} BDT"
                        : "Not set";

                    msg +=
                        $"──────────────────────────\n" +
                        $"Name: {d.FullName}\n" +
                        $"Specialty: {d.Specialization}\n" +
                        $"Qualification: {qualificationText}\n" +
                        $"Consultation Fee: {feeText}\n" +
                        $"──────────────────────────\n\n";
                }

                msg +=
                    "Reply with the doctor’s name to continue booking the appointment.";

                return msg;
            }
        }
        return null;
    }
}
