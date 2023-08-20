﻿using System.ComponentModel.DataAnnotations;

namespace Mango.Services.ProductAPI.Dtos
{
    public class ProductDto
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Description { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string ImageUrl { get; set; } = string.Empty;
    }
}
