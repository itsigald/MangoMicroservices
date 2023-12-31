﻿using AutoMapper;
using Mango.MessageBus;
using Mango.Services.ProductAPI.Data;
using Mango.Services.ShoppingCartAPI.Dtos;
using Mango.Services.ShoppingCartAPI.Models;
using Mango.Services.ShoppingCartAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace Mango.Services.ShoppingCartAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CartAPIController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly IProductService _productService;
        private readonly ICouponService _couponService;
        private readonly IMessageBus _messageBus;
        private readonly IMessageBusSetting _messageBusSetting;
        private ResponseDto _responseDto;

        public CartAPIController(AppDbContext context, IMapper mapper, IProductService productService, ICouponService couponService, IMessageBus messageBus, IMessageBusSetting messageBusSetting)
        {
            _context = context;
            _mapper = mapper;
            _productService = productService;
            _couponService = couponService;
            _messageBus = messageBus;
            _messageBusSetting = messageBusSetting;
            _responseDto = new ResponseDto();
        }

        [HttpGet("GetCart/{userId}/{cartId:int}")]
        public async Task<ResponseDto> GetCartById(string userId, int cartId)
        {
            CartDto cartDto = new CartDto();

            try
            {
                var cartHeader = _mapper.Map<CartHeaderDto>(await _context.CartHeaders
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.IsDeleted == false && x.CartHeaderId == cartId));

                if(cartHeader != null)
                {
                    var cartDetails = _mapper.Map<IEnumerable<CartDetailsDto>>(await _context.CartDetails
                        .Where(x => x.IsDeleted == false && x.CartHeaderId == cartId)
                        .ToListAsync());

                    foreach (var item in cartDetails)
                    {
                        item.Product = await _productService.GetProduct(item.ProductId);
                        cartHeader.CartTotal += item.Quantity * (item.Product == null ? 0 : item.Product.Price);
                    }

                    if (!string.IsNullOrEmpty(cartHeader.CouponCode))
                    {
                        CouponDto coupon = await _couponService.GetCoupon(cartHeader.CouponCode);

                        if (coupon != null && cartHeader.CartTotal > coupon.MinAmount)
                        {
                            cartHeader.CartTotal -= Convert.ToDecimal(coupon.DiscountAmount);
                            cartHeader.Discount = Convert.ToDecimal(coupon.DiscountAmount);
                        }
                    }

                    cartDto.CartHeader = cartHeader;
                    cartDto.CartDetails = cartDetails;

                    _responseDto.Result = cartDto;
                }
                else
                {
                    _responseDto.Result = null;
                }
            }
            catch (Exception ex)
            {
                _responseDto.IsSuccess = false;
                _responseDto.Message = ex.Message;
            }

            return _responseDto;
        }

        [HttpGet("GetCart/{userId}")]
        public async Task<ResponseDto> GetCart(string userId)
        {
            try
            {
                var cartHeadersDto = _mapper.Map<CartHeaderDto>(await _context.CartHeaders
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.IsDeleted == false && x.IsOpen == true));

                var cartDetailsDB = await _context.CartDetails
                    .Where(x => x.CartHeaderId == cartHeadersDto.CartHeaderId && x.IsDeleted == false)
                    .ToListAsync();

                var cartDetailsDto = _mapper.Map<IEnumerable<CartDetailsDto>>(cartDetailsDB);

                var products = await _productService.GetProducts();

                foreach (var item in cartDetailsDto)
                {
                    item.Product = products.FirstOrDefault(x => x.ProductId == item.ProductId);
                    cartHeadersDto.CartTotal += item.Quantity * (item.Product == null ? 0 : item.Product.Price);
                }

                if(!string.IsNullOrEmpty(cartHeadersDto.CouponCode))
                {
                    CouponDto coupon = await _couponService.GetCoupon(cartHeadersDto.CouponCode);

                    if(coupon != null && cartHeadersDto.CartTotal > coupon.MinAmount)
                    {
                        cartHeadersDto.CartTotal -= Convert.ToDecimal(coupon.DiscountAmount);
                        cartHeadersDto.Discount = Convert.ToDecimal(coupon.DiscountAmount);
                    }
                }

                _responseDto.Result = new CartDto
                {
                    CartHeader = cartHeadersDto,
                    CartDetails = cartDetailsDto
                };
            }
            catch (Exception ex)
            {
                _responseDto.IsSuccess = false;
                _responseDto.Message = ex.Message;
            }

            return _responseDto;
        }

        [HttpPost("CartUpsert")]
        public async Task<ResponseDto> CartUpsert([FromBody] CartDto cartDto)
        {
            if(cartDto == null || cartDto.CartHeader == null || cartDto.CartDetails == null || cartDto.CartDetails.Count() == 0)
            {
                _responseDto.Message = "The cart is empty";
            }
            else
            {
                try
                {
                    await using var transaction = _context.Database.CurrentTransaction == null
                            ? await _context.Database.BeginTransactionAsync()
                            : _context.Database.UseTransaction(_context.Database.CurrentTransaction.GetDbTransaction());

                    var cartHeader = await _context.CartHeaders
                        .FirstOrDefaultAsync(c => c.UserId == cartDto.CartHeader.UserId && c.IsOpen == true);
                    
                    if(cartHeader == null)
                    {
                        CartHeader cartH = _mapper.Map<CartHeader>(cartDto.CartHeader);

                        cartH.CartInsert = DateTime.Now;
                        cartH.IsOpen = true;

                        _context.CartHeaders.Add(cartH);
                        await _context.SaveChangesAsync();

                        cartDto.CartHeader = _mapper.Map<CartHeaderDto>(cartH);

                        if (cartDto.CartDetails != null && cartDto.CartDetails.Count() > 0)
                        {
                            var cardDetails = _mapper.Map<IEnumerable<CartDetails>>(cartDto.CartDetails);

                            foreach (var cd in cardDetails)
                            {
                                cd.CartHeaderId = cartH.CartHeaderId;
                                _context.CartDetails.Add(cd);
                            }

                            await _context.SaveChangesAsync();

                            cartDto.CartDetails = _mapper.Map<IEnumerable<CartDetailsDto>>(cardDetails);
                        }
                    }
                    else
                    {
                        var cartDetails = await _context.CartDetails.Where(c => c.CartHeaderId == cartHeader.CartHeaderId).ToListAsync();

                        var detailsInDb = cartDetails.Select(c => c.ProductId).ToList();
                        var detailsCart = cartDto.CartDetails.Select(d => d.ProductId).ToList();

                        bool areEqual = detailsInDb.Exists(p => detailsCart.Contains(p));

                        if (areEqual)
                        {
                            foreach (var cd in cartDto.CartDetails)
                            {
                                var cartItem = cartDetails.FirstOrDefault(i => i.ProductId == cd.ProductId);

                                if(cartItem != null)
                                {
                                    cartItem.Quantity = cd.Quantity;
                                    cartItem.IsDeleted = false;
                                    _context.CartDetails.Update(cartItem);
                                }
                            }

                            await _context.SaveChangesAsync();
                        }
                        else
                        {
                            var cartDetailDtosNew = cartDto.CartDetails.Where(p => !cartDetails.Exists(z => z.ProductId == p.ProductId)).ToList();
                            var cartDetailsNew = _mapper.Map<IEnumerable<CartDetails>>(cartDetailDtosNew);

                            foreach (var item in cartDetailsNew)
                            {
                                item.CartHeaderId = cartHeader.CartHeaderId;
                                _context.CartDetails.Add(item);
                            }
                            
                            await _context.SaveChangesAsync();
                            cartDto.CartDetails = _mapper.Map<IEnumerable<CartDetailsDto>>(cartDetailsNew);
                        }
                    }

                    if(transaction != null)
                        await transaction.CommitAsync();

                    _responseDto.Result = cartDto;
                }
                catch (Exception ex)
                {
                    _responseDto.IsSuccess = false;
                    _responseDto.Message = ex.Message;
                }
            }

            return _responseDto;
        }

        [HttpPost("RemoveCart")]
        public async Task<ResponseDto> RemoveCart([FromBody] int cartDetailsId)
        {
            try
            {
                var cartDetails = await _context.CartDetails.FirstOrDefaultAsync(c => c.CartDetailsId == cartDetailsId);

                if(cartDetails != null)
                {
                    int countItemInDb = await _context.CartDetails.CountAsync(p => p.CartHeaderId == cartDetails.CartHeaderId && p.IsDeleted == false);

                    cartDetails.IsDeleted = true;
                    _context.CartDetails.Update(cartDetails);

                    if(countItemInDb == 1)
                    {
                        var cartHhader = await _context.CartHeaders.FirstOrDefaultAsync(c => c.CartHeaderId.Equals(cartDetails.CartHeaderId));

                        if(cartHhader != null)
                        {
                            cartHhader.IsDeleted = true;
                            _context.CartHeaders.Update(cartHhader!);
                        }
                    }

                    await _context.SaveChangesAsync();

                    _responseDto.Result = true;
                }
                else
                {
                    _responseDto.IsSuccess = false;
                    _responseDto.Message = "No Cart Detail Was Found";
                }
            }
            catch (Exception ex)
            {
                _responseDto.IsSuccess = false;
                _responseDto.Message = ex.Message;
            }

            return _responseDto;
        }

        [HttpPost("ApplyCoupon")]
        public async Task<ResponseDto> ApplyCoupon([FromBody] ApplyCouponDto applyCouponDto)
        {
            try
            {
                var cartFromDb = await _context.CartHeaders.FirstOrDefaultAsync(c => c.UserId == applyCouponDto.UserId && c.CartHeaderId == applyCouponDto.CartHeaderId);

                if (cartFromDb != null)
                {
                    cartFromDb.CouponCode = applyCouponDto.CouponCode;
                    _context.CartHeaders.Update(cartFromDb);
                    await _context.SaveChangesAsync();
                    
                    _responseDto.Result = true;
                }
                else
                {
                    _responseDto.IsSuccess = false;
                    _responseDto.Message = $"The Cart doesn't exists";
                }
            }
            catch (Exception ex)
            {
                _responseDto.IsSuccess = false;
                _responseDto.Message = ex.Message;
            }

            return _responseDto;
        }

        [HttpPost("EmailCartRequest")]
        public async Task<ResponseDto> EmailCartRequest([FromBody] CartDto cartDto)
        {
            try
            {
                await _messageBus.PublishMessage(_messageBusSetting.TopicQueueName.EmailShoppingCart, cartDto);
                _responseDto.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _responseDto.IsSuccess = false;
                _responseDto.Message = ex.Message;
            }

            return _responseDto;
        }
    }
}
