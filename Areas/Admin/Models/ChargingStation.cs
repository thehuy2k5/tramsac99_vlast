using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace tramsac99.Areas.Admin.Models
{
    public class ChargingStation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string? Name { get; set; }

        [Required]
        [StringLength(300)]
        public string? Address { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [Required]
        [StringLength(50)]
        public string? Status { get; set; } = ChargingStatus.Active;

        [StringLength(100)]
        public string? ChargerType { get; set; }

        [StringLength(50)]
        public string? Power { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PricePerKwh { get; set; }

        public ICollection<StationReview> Reviews { get; set; } = new List<StationReview>();

        public ICollection<ChargingPole> ChargingPoles { get; set; } = new List<ChargingPole>(); // Why changed: one station has many poles

        // Why changed: track station ownership so the user can manage only their own stations.
        public int? OwnerUserId { get; set; }

        [ForeignKey(nameof(OwnerUserId))]
        public AppUser? OwnerUser { get; set; }

        [NotMapped]
        public string GoogleMapUrl => $"https://www.google.com/maps?q={Latitude},{Longitude}";

        
    }
}