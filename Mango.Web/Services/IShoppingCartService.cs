﻿using Mango.Web.Models;

namespace Mango.Web.Services
{
    public interface IShoppingCartService
    {
        Task<ResponseDto?> GetShoppingCartAsync(string userId, int cartId);
        Task<ResponseDto?> GetShoppingCartsAsync(string userId);
        Task<ResponseDto?> UpsertShoppingCartAsync(CartDto cartDto);
        Task<ResponseDto?> RemoveShoppingCartAsync(int cartId);
        Task<ResponseDto?> ApplyCouponAsync(ApplyCouponDto applyCouponDto);
        Task<ResponseDto?> EmailCartAsync(CartDto cartDto);
    }
}
