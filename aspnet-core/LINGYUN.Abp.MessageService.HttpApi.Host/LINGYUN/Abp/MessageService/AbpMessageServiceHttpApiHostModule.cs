﻿using DotNetCore.CAP;
using IdentityModel;
using LINGYUN.Abp.EventBus.CAP;
using LINGYUN.Abp.IM.SignalR;
using LINGYUN.Abp.MessageService.EntityFrameworkCore;
using LINGYUN.Abp.MessageService.MultiTenancy;
using LINGYUN.Abp.Notifications.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using System;
using Volo.Abp;
using Volo.Abp.AspNetCore.Authentication.JwtBearer;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.Autofac;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Volo.Abp.VirtualFileSystem;

namespace LINGYUN.Abp.MessageService
{
    [DependsOn(
        typeof(AbpMessageServiceHttpApiModule),
        typeof(AbpAspNetCoreMultiTenancyModule),
        typeof(AbpMessageServiceEntityFrameworkCoreModule),
        typeof(AbpTenantManagementEntityFrameworkCoreModule),
        typeof(AbpSettingManagementEntityFrameworkCoreModule),
        typeof(AbpPermissionManagementEntityFrameworkCoreModule),
        typeof(AbpAspNetCoreAuthenticationJwtBearerModule),
        typeof(AbpIMSignalRModule),
        typeof(AbpNotificationsSignalRModule),
        typeof(AbpCAPEventBusModule),
        typeof(AbpAutofacModule)
        )]
    public class AbpMessageServiceHttpApiHostModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            var configuration = context.Services.GetConfiguration();

            PreConfigure<CapOptions>(options =>
            {
                options
                .UseMySql(configuration.GetConnectionString("Default"))
                .UseRabbitMQ(rabbitMQOptions =>
                {
                    configuration.GetSection("CAP:RabbitMQ").Bind(rabbitMQOptions);
                });
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var hostingEnvironment = context.Services.GetHostingEnvironment();
            var configuration = hostingEnvironment.BuildConfiguration();
            // 配置Ef
            Configure<AbpDbContextOptions>(options =>
            {
                options.UseMySQL();
            });

            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<AbpMessageServiceHttpApiHostModule>("LINGYUN.Abp.MessageService");
            });

            // 多租户
            Configure<AbpMultiTenancyOptions>(options =>
            {
                options.IsEnabled = true;
            });

            Configure<AbpTenantResolveOptions>(options =>
            {
                options.TenantResolvers.Insert(0, new AuthorizationTenantResolveContributor());
            });

            // Swagger
            context.Services.AddSwaggerGen(
                options =>
                {
                    options.SwaggerDoc("v1", new OpenApiInfo { Title = "MessageService API", Version = "v1" });
                    options.DocInclusionPredicate((docName, description) => true);
                    options.CustomSchemaIds(type => type.FullName);
                });

            // 支持本地化语言类型
            Configure<AbpLocalizationOptions>(options =>
            {
                options.Languages.Add(new LanguageInfo("en", "en", "English"));
                options.Languages.Add(new LanguageInfo("zh-Hans", "zh-Hans", "简体中文"));
            });

            context.Services.AddAuthentication("Bearer")
                .AddIdentityServerAuthentication(options =>
                {
                    options.Authority = configuration["AuthServer:Authority"];
                    options.RequireHttpsMetadata = false;
                    options.ApiName = configuration["AuthServer:ApiName"];
                    AbpClaimTypes.UserId = JwtClaimTypes.Subject;
                    AbpClaimTypes.UserName = JwtClaimTypes.Name;
                    AbpClaimTypes.Role = JwtClaimTypes.Role;
                    AbpClaimTypes.Email = JwtClaimTypes.Email;
                });

            context.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = configuration["RedisCache:ConnectString"];
                var instanceName = configuration["RedisCache:RedisPrefix"];
                options.InstanceName = instanceName.IsNullOrEmpty() ? "MessageService_Cache" : instanceName;
            });

            if (!hostingEnvironment.IsDevelopment())
            {
                var redis = ConnectionMultiplexer.Connect(configuration["RedisCache:ConnectString"]);
                context.Services
                    .AddDataProtection()
                    .PersistKeysToStackExchangeRedis(redis, "MessageService-Protection-Keys");
            }
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var app = context.GetApplicationBuilder();
            // http调用链
            app.UseCorrelationId();
            // 虚拟文件系统
            app.UseVirtualFiles();
            // 本地化
            app.UseAbpRequestLocalization();
            //路由
            app.UseRouting();
            // 加入自定义中间件
            app.UseMiddleware<SignalRJwtTokenMiddleware>();
            // 认证
            app.UseAuthentication();
            // jwt
            app.UseJwtTokenMiddleware();
            // 授权
            app.UseAuthorization();
            // 多租户
            app.UseMultiTenancy();
            // Swagger
            app.UseSwagger();
            // Swagger可视化界面
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Support MessageService API");
            });
            // 审计日志
            app.UseAuditing();
            // 路由
            app.UseConfiguredEndpoints();
        }
    }
}
