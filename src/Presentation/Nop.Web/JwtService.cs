﻿using System;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Nop.Services.Logging;

namespace Nop.Web
{
    public class JwtService
    {
        private readonly ILogger _logger;
        private readonly string _issuer;
        private readonly string _secret;
        private readonly int _expirationInMinutes;

        public JwtService(IConfiguration config, ILogger logger)
        {
            _logger = logger;
            _issuer = config.GetSection("Jwt").GetValue<string>("Issuer");
            _secret = config.GetSection("Jwt").GetValue<string>("Key");
            _expirationInMinutes = config.GetSection("Jwt").GetValue<int>("expirationInMinutes");
        }

        public string GenerateSecurityToken(string email, int customerId)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var permClaims = new List<Claim>();
            permClaims.Add(new Claim("Email", email));
            permClaims.Add(new Claim("UserId", customerId.ToString()));

            var token = new JwtSecurityToken(_issuer,
              _issuer,
              claims: permClaims,
              null,
              expires: DateTime.Now.AddMinutes(_expirationInMinutes),
              signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public (bool IsValid, string? EmailClaim, string? UserIdClaim) ValidateSecurityToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters()
            {
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret)),
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
            };

            try
            {
                var claimsPrincipal = new JwtSecurityTokenHandler().ValidateToken(token, tokenValidationParameters, 
                    out var validatedToken);
                
                return (true, 
                    claimsPrincipal.Claims.First(c => c.Type == "Email").Value, 
                    claimsPrincipal.Claims.First(c => c.Type == "UserId").Value
                    );
            }
            catch (Exception e)
            {
                _logger.ErrorAsync("Validation of token failed", e);
                return (false, null, null);
            }
        }
    }
}
