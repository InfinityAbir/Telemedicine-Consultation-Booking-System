# 🩺 TeleMed – Online Healthcare Management System

**TeleMed** is a modern web-based telemedicine platform built with **ASP.NET Core MVC**.  
It connects doctors and patients for **virtual consultations**, enabling features like **online appointments, prescription management, video sessions**, and **role-based access control** for Admins, Doctors, and Patients.

---
## 📸 Screenshots

![Home](screenshots/home.png)
![Doctor Dashboard](screenshots/doctor-dashboard.png)
![Appointment](screenshots/appointment.png)

---

## 🚀 Features

### 👨‍⚕️ For Doctors
- Manage appointments and schedules
- Approve or reject patient bookings
- Conduct online consultationsd
- Upload, view, and update digital prescriptions
- See patient feedback and ratings

### 🧑‍🤝‍🧑 For Patients
- Book and manage appointments
- Receive instant email confirmations
- Join virtual sessions with doctors
- View and download uploaded prescriptions
- Provide feedback after consultations
- Download invoices in PDF format
- Access profile and appointment history

### 🧑‍💼 For Admins
- Manage doctors and patient lists
- Approve pending doctor and schedule requests
- View reports and payment summaries

### ⚡ System-Level Features
- Automatically generates professional PDF invoices
- Patients can download invoices anytime
- Sends email notifications for appointment bookings
- Daily automated database backups every 24 hours
- Role-based authentication using ASP.NET Identity
- Structured and secure payment flow

---

## 🛠️ Tech Stack

| Category | Technologies |
|-----------|---------------|
| **Framework** | ASP.NET Core MVC 9 |
| **Frontend** | Razor Pages, Bootstrap 5, jQuery |
| **Database** | Entity Framework Core (SQL Server) |
| **Authentication** | ASP.NET Identity (Role-based) |
| **PDF Generator** | QuestPDF |
| **File Storage** | wwwroot/uploads/prescriptions/ |

---

## ⚙️ Installation & Setup

### 1️⃣ Clone the repository
    git clone https://github.com/infinityAbir/Telemedicine-Consultation-Booking-System.git
    cd TeleMed
### 2️⃣ Open in Visual Studio
Open the .sln file in Visual Studio 2022 or later.
Make sure you have .NET 8 SDK installed.

### 3️⃣ Configure Database
Update your appsettings.json connection string if needed.
Run migrations and update the database:
      
      git clone https://github.com/infinityAbir/Telemedicine-Consultation-Booking-System.git
      cd TeleMed
### 4️⃣ Run the Application
Press F5 or run:
     
      dotnet run
      
Visit https://localhost:5001 (or the port shown in console).

### 💳 Payment Gateway Configuration

Before using the online payment system, add your payment gateway keys to your **appsettings.json** file:

    ```json
    "StripeSettings": {
      "PublishableKey": "YOUR_PUBLISHABLE_KEY_HERE",
      "SecretKey": "YOUR_SECRET_KEY_HERE"
    }
---
### 📧 Email Integration Setup

To enable automated emails (appointment confirmations, receipts, notifications), add your email configuration to **appsettings.json**:

    ```json
    "EmailSettings": {
      "SenderEmail": "YOUR_EMAIL_ADDRESS_HERE",
      "AppPassword": "YOUR_APP_PASSWORD_HERE",
      "Host": "smtp.gmail.com",
      "Port": 587
    }
---

👨‍💻 Author
Abir Hasan
