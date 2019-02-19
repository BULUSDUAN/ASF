﻿using ASF.Domain.Entities;
using ASF.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace ASF.Domain.Services
{
    /// <summary>
    /// 账号权限认证服务
    /// </summary>
    public class AccountAuthorizationService
    {
        private readonly IRoleRepository _roleRepository;
        private readonly IPermissionRepository _permissionRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public AccountAuthorizationService(IRoleRepository roleRepository, IPermissionRepository permissionRepository, IHttpContextAccessor httpContextAccessor, IServiceProvider serviceProvider, ILogger logger)
        {
            _roleRepository = roleRepository;
            _permissionRepository = permissionRepository;
            _httpContextAccessor = httpContextAccessor;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Result<Permission> Authentication()
        {
            HttpContext context = _httpContextAccessor.HttpContext;
            HttpRequest request = context.Request;
            var roles = context.User.FindFirst("roles")?.Value;
            var requestPath = request.PathBase + request.Path;

            if (string.IsNullOrEmpty(roles))
            {
                this._logger.LogDebug("Access to Tokan needs to include roles");
                return Result<Permission>.ReFailure(ResultCodes.NotAcceptable);
            }

            //根据请求地址获取权限
            var parmission = _permissionRepository.GetByApiAddress(requestPath).GetAwaiter().GetResult();
            if (parmission==null)
            {
                this._logger.LogWarning($"Did not find the corresponding permissions of {requestPath}");
                return Result<Permission>.ReFailure(ResultCodes.NotAcceptable);
            }
            if (!parmission.IsNormal())
            {
                this._logger.LogWarning($"{parmission.Name} permissions are not available");
                return Result<Permission>.ReFailure(ResultCodes.NotAcceptable);
            }

            //判断是否为超级管理员
            if (roles.Equals("ALL"))
            {
                //获取超级管理员账号
                int uid = context.User.UserId();
                var account = this._serviceProvider.GetRequiredService<IAccountRepository>().GetAsync(uid).GetAwaiter().GetResult();
                if(account==null)
                {
                    this._logger.LogWarning($"{uid} Super administrator does not exist");
                    return Result<Permission>.ReFailure(ResultCodes.NotAcceptable);
                }
                else if(account.IsSuperAdministrator())
                {
                    return Result<Permission>.ReSuccess(parmission);
                }
            }
            else
            {
                //获取登录账户分配的角色集
                var ridList = this.AnalysisRoleId(roles);

                //根据ID获取角色
                var roleList = this._roleRepository.GetList(ridList).GetAwaiter().GetResult();
                if (roleList == null)
                {
                    this._logger.LogWarning($"No authorized roles found");
                    return Result<Permission>.ReFailure(ResultCodes.NotAcceptable);
                }
                foreach (var role in roleList)
                {
                    if (!role.IsNormal())
                    {
                        this._logger.LogWarning($"{role.Name} role are not available");
                        return Result<Permission>.ReFailure(ResultCodes.NotAcceptable);
                    }
                    //如果包含此权限，者返回成功
                    if (role.ContainPermission(parmission.Id))
                    {
                        return Result<Permission>.ReSuccess(parmission);
                    }
                }
            }
            this._logger.LogWarning($"Authorized users are not assigned {parmission.Name} permissions ");
            return Result<Permission>.ReFailure(ResultCodes.NotAcceptable);
        }


        /// <summary>
        /// 解析Token中的角色集
        /// </summary>
        /// <param name="roles">角色集标识</param>
        /// <returns></returns>
        private IList<int> AnalysisRoleId(string roles)
        {
            List<int> rolesId = new List<int>();
            foreach (var id in roles.Split(','))
            {
                if (string.IsNullOrEmpty(id))
                    continue;

                rolesId.Add(Convert.ToInt32(id));
            }
            return rolesId;
        }
    }
}
