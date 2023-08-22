﻿namespace Mango.Services.EmailAPI.Data
{
    public class CartDto
    {
        public CartHeaderDto CartHeader { get; set; } = new();
        public IEnumerable<CartDetailsDto>? CartDetails { get; set; }
        public EmailInfoDto EmailInfoDto { get; set; } = new();
    }
}
