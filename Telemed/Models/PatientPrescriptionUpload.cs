using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class PatientPrescriptionUpload
{
    public int Id { get; set; }

    [Required]
    public string PatientId { get; set; }  // FK to AspNetUsers.Id

    [Required]
    [StringLength(260)]
    public string OriginalFileName { get; set; }

    [Required]
    [StringLength(260)]
    public string StoredFileName { get; set; } // GUID + extension

    [Required]
    public DateTime UploadDate { get; set; }

    [StringLength(500)]
    public string Description { get; set; }

    // optional: file MIME type
    [StringLength(100)]
    public string ContentType { get; set; }

    // optional: file size in bytes
    public long FileSize { get; set; }
}
