﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Bringin;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("plugins/{storeId}/Bringin")]
public class BringinController : Controller
{
    private readonly BringinService _bringinService;
    private readonly IHttpClientFactory _httpClientFactory;

    public BringinController(BringinService bringinService, IHttpClientFactory httpClientFactory)
    {
        _bringinService = bringinService;
        _httpClientFactory = httpClientFactory;
    }
    
    [HttpGet("onboard")]
    public async Task<IActionResult> Onboard(string storeId)
    {
        
       
        var vm = await _bringinService.Update(storeId);
        
        var callbackUri = Url.Action("Callback", "Bringin", new
        {
            code = vm.Code,
            storeId
        }, Request.Scheme);
        
        var httpClient = BringinClient.CreateClient(_httpClientFactory);
        var onboardUri = await BringinClient.OnboardUri(httpClient, new Uri(callbackUri));
        return Redirect(onboardUri.ToString());
    }

    [HttpGet("")]
    public async Task<IActionResult> Edit()
    {
        return View();
    }
    
    [HttpPost("callback")]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string storeId, string code, [FromBody]BringinVerificationUpdate content)
    {
        var vm = await _bringinService.Update(storeId);
        if(vm.Code != code) return BadRequest();
        if(content.verificationStatus != "APPROVED") return BadRequest("Verification not approved");

        if (string.IsNullOrEmpty(vm.ApiKey) && !string.IsNullOrEmpty(content.apikey))
        {
            vm.ApiKey = content.apikey;
            await _bringinService.Update(storeId, vm);
           
        }
        return Ok();
    }
    
    public class BringinVerificationUpdate
    {
        public string userId { get; set; }
        public string apikey { get; set; }
        public string verificationStatus { get; set; }
    }


    

    // [HttpGet("callback")]
    // public async Task<IActionResult> Callback(string storeId, string apiKey, string code)
    // {
    //     //truncate with showing only first 3 letters on start ond end
    //
    //     var truncatedApikey = apiKey.Substring(0, 3) + "***" + apiKey.Substring(apiKey.Length - 3);
    //
    //     return View("Confirm",
    //         new ConfirmModel("Confirm Bringin API Key",
    //             $"You are about to set your Bringin API key to {truncatedApikey}", "Set", "btn-primary"));
    // }
    //
    // [HttpPost("callback")]
    // public async Task<IActionResult> CallbackConfirm(string storeId, string apiKey)
    // {
    //     var vm = await _bringinService.Update(storeId);
    //     vm.ApiKey = apiKey;
    //     await _bringinService.Update(storeId, vm);
    //     return RedirectToAction("Edit", new {storeId});
    // }
}