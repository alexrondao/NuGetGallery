﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Mvc;
using NuGetGallery.Authentication;
using NuGetGallery.Configuration;

namespace NuGetGallery
{
    public partial class UsersController
        : AppController
    {
        public ICuratedFeedService CuratedFeedService { get; protected set; }
        public IGalleryConfigurationService ConfigService { get; protected set; }
        public IUserService UserService { get; protected set; }
        public IMessageService MessageService { get; protected set; }
        public IPackageService PackageService { get; protected set; }
        public AuthenticationService AuthService { get; protected set; }

        public UsersController(
            ICuratedFeedService feedsQuery,
            IGalleryConfigurationService configService,
            IUserService userService,
            IPackageService packageService,
            IMessageService messageService,
            AuthenticationService authService)
        {
            CuratedFeedService = feedsQuery;
            ConfigService = configService;
            UserService = userService;
            PackageService = packageService;
            MessageService = messageService;
            AuthService = authService;
        }

        [HttpGet]
        [Authorize]
        public virtual ActionResult ConfirmationRequired()
        {
            User user = GetCurrentUser();
            var model = new ConfirmationViewModel
            {
                ConfirmingNewAccount = !(user.Confirmed),
                UnconfirmedEmailAddress = user.UnconfirmedEmailAddress,
            };
            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ActionName("ConfirmationRequired")]
        [ValidateAntiForgeryToken]
        public virtual ActionResult ConfirmationRequiredPost()
        {
            User user = GetCurrentUser();
            var confirmationUrl = Url.ConfirmationUrl(
                "Confirm", "Users", user.Username, user.EmailConfirmationToken);

            var alreadyConfirmed = user.UnconfirmedEmailAddress == null;

            ConfirmationViewModel model;
            if (!alreadyConfirmed)
            {
                MessageService.SendNewAccountEmail(new MailAddress(user.UnconfirmedEmailAddress, user.Username), confirmationUrl);

                model = new ConfirmationViewModel
                {
                    ConfirmingNewAccount = !(user.Confirmed),
                    UnconfirmedEmailAddress = user.UnconfirmedEmailAddress,
                    SentEmail = true,
                };
            }
            else
            {
                model = new ConfirmationViewModel {AlreadyConfirmed = true};
            }
            return View(model);
        }

        [HttpGet]
        [Authorize]
        public virtual async Task<ActionResult> Account()
        {
            return await AccountView(new AccountViewModel());
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<ActionResult> ChangeEmailSubscription(bool? emailAllowed, bool? notifyPackagePushed)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return HttpNotFound();
            }
            
            await UserService.ChangeEmailSubscriptionAsync(user, 
                emailAllowed.HasValue && emailAllowed.Value, 
                notifyPackagePushed.HasValue && notifyPackagePushed.Value);

            TempData["Message"] = Strings.EmailPreferencesUpdated;
            return RedirectToAction("Account");
        }

        [HttpGet]
        [Authorize]
        public virtual ActionResult Thanks()
        {
            // No need to redirect here after someone logs in...
            // By having this value present in the dictionary BUT null, we don't put "returnUrl" on the Login link at all
            ViewData[Constants.ReturnUrlViewDataKey] = null;
            return View();
        }

        [HttpGet]
        [Authorize]
        public virtual ActionResult Packages()
        {
            var user = GetCurrentUser();
            var packages = PackageService.FindPackagesByOwner(user, includeUnlisted: true)
                .Select(p => new PackageViewModel(p)
                {
                    DownloadCount = p.PackageRegistration.DownloadCount,
                    Version = null
                }).ToList();

            var model = new ManagePackagesViewModel
            {
                Packages = packages
            };
            return View(model);
        }

        [HttpGet]
        public virtual ActionResult ForgotPassword()
        {
            // We don't want Login to have us as a return URL
            // By having this value present in the dictionary BUT null, we don't put "returnUrl" on the Login link at all
            ViewData[Constants.ReturnUrlViewDataKey] = null;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<ActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            // We don't want Login to have us as a return URL
            // By having this value present in the dictionary BUT null, we don't put "returnUrl" on the Login link at all
            ViewData[Constants.ReturnUrlViewDataKey] = null;

            if (ModelState.IsValid)
            {
                var user = await AuthService.GeneratePasswordResetToken(model.Email, Constants.DefaultPasswordResetTokenExpirationHours * 60);
                if (user != null)
                {
                    return SendPasswordResetEmail(user, forgotPassword: true);
                }

                ModelState.AddModelError("Email", Strings.CouldNotFindAnyoneWithThatEmail);
            }

            return View(model);
        }

        [HttpGet]
        public virtual ActionResult PasswordSent()
        {
            // We don't want Login to have us as a return URL
            // By having this value present in the dictionary BUT null, we don't put "returnUrl" on the Login link at all
            ViewData[Constants.ReturnUrlViewDataKey] = null;

            ViewBag.Email = TempData["Email"];
            ViewBag.Expiration = Constants.DefaultPasswordResetTokenExpirationHours;
            return View();
        }

        [HttpGet]
        public virtual ActionResult ResetPassword(bool forgot)
        {
            // We don't want Login to have us as a return URL
            // By having this value present in the dictionary BUT null, we don't put "returnUrl" on the Login link at all
            ViewData[Constants.ReturnUrlViewDataKey] = null;

            ViewBag.ResetTokenValid = true;
            ViewBag.ForgotPassword = forgot;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<ActionResult> ResetPassword(string username, string token, PasswordResetViewModel model, bool forgot)
        {
            // We don't want Login to have us as a return URL
            // By having this value present in the dictionary BUT null, we don't put "returnUrl" on the Login link at all
            ViewData[Constants.ReturnUrlViewDataKey] = null;

            ViewBag.ForgotPassword = forgot;

            Credential credential = null;
            try
            {
                credential = await AuthService.ResetPasswordWithToken(username, token, model.NewPassword);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }

            ViewBag.ResetTokenValid = credential != null;

            if (!ViewBag.ResetTokenValid)
            {
                ModelState.AddModelError("", Strings.InvalidOrExpiredPasswordResetToken);
                return View(model);
            }

            if (credential != null && !forgot)
            {
                // Setting a password, so notify the user
                MessageService.SendCredentialAddedNotice(credential.User, credential);
            }

            return RedirectToAction(
                actionName: "PasswordChanged",
                controllerName: "Users");
        }

        [Authorize]
        public virtual async Task<ActionResult> Confirm(string username, string token)
        {
            // We don't want Login to have us as a return URL
            // By having this value present in the dictionary BUT null, we don't put "returnUrl" on the Login link at all
            ViewData[Constants.ReturnUrlViewDataKey] = null;

            if (!String.Equals(username, User.Identity.Name, StringComparison.OrdinalIgnoreCase))
            {
                return View(new ConfirmationViewModel
                    {
                        WrongUsername = true,
                        SuccessfulConfirmation = false,
                    });
            }

            var user = GetCurrentUser();

            var alreadyConfirmed = user.UnconfirmedEmailAddress == null;

            string existingEmail = user.EmailAddress;
            var model = new ConfirmationViewModel
            {
                ConfirmingNewAccount = String.IsNullOrEmpty(existingEmail),
                SuccessfulConfirmation = !alreadyConfirmed,
                AlreadyConfirmed = alreadyConfirmed
            };

            if (!alreadyConfirmed)
            {

                try
                {
                    if (!(await UserService.ConfirmEmailAddress(user, token)))
                    {
                        model.SuccessfulConfirmation = false;
                    }
                }
                catch (EntityException)
                {
                    model.SuccessfulConfirmation = false;
                    model.DuplicateEmailAddress = true;
                }

                // SuccessfulConfirmation is required so that the confirm Action isn't a way to spam people.
                // Change notice not required for new accounts.
                if (model.SuccessfulConfirmation && !model.ConfirmingNewAccount)
                {
                    MessageService.SendEmailChangeNoticeToPreviousEmailAddress(user, existingEmail);

                    string returnUrl = HttpContext.GetConfirmationReturnUrl();
                    if (!String.IsNullOrEmpty(returnUrl))
                    {
                        TempData["Message"] = "You have successfully confirmed your email address!";
                        return SafeRedirect(returnUrl);
                    }
                }
            }

            return View(model);
        }

        [HttpGet]
        public virtual ActionResult Profiles(string username, int page = 1, bool showAllPackages = false)
        {
            var user = UserService.FindByUsername(username);
            if (user == null)
            {
                return HttpNotFound();
            }

            var packages = PackageService.FindPackagesByOwner(user, includeUnlisted: false)
                .OrderByDescending(p => p.PackageRegistration.DownloadCount)
                .Select(p => new PackageViewModel(p)
                {
                    DownloadCount = p.PackageRegistration.DownloadCount
                }).ToList();

            var model = new UserProfileModel(user, packages, page - 1, Constants.DefaultPackageListPageSize, Url);
            model.ShowAllPackages = showAllPackages;

            return View(model);
        }

        [HttpPost]
        [Authorize]
        public virtual async Task<ActionResult> ChangeEmail(AccountViewModel model)
        {
            if (!ModelState.IsValidField("ChangeEmail.NewEmail"))
            {
                return await AccountView(model);
            }

            var user = GetCurrentUser();
            if (user.HasPassword())
            {
                if (!ModelState.IsValidField("ChangeEmail.Password"))
                {
                    return await AccountView(model);
                }

                var authUser = await AuthService.Authenticate(User.Identity.Name, model.ChangeEmail.Password);
                if (authUser == null)
                {
                    ModelState.AddModelError("ChangeEmail.Password", Strings.CurrentPasswordIncorrect);
                    return await AccountView(model);
                }
            }
            // No password? We can't do any additional verification...

            if (String.Equals(model.ChangeEmail.NewEmail, user.LastSavedEmailAddress, StringComparison.OrdinalIgnoreCase))
            {
                // email address unchanged - accept
                return RedirectToAction(actionName: "Account", controllerName: "Users");
            }

            try
            {
                await UserService.ChangeEmailAddress(user, model.ChangeEmail.NewEmail);
            }
            catch (EntityException e)
            {
                ModelState.AddModelError("ChangeEmail.NewEmail", e.Message);
                return await AccountView(model);
            }

            if (user.Confirmed)
            {
                var confirmationUrl = Url.ConfirmationUrl(
                    "Confirm", "Users", user.Username, user.EmailConfirmationToken);
                MessageService.SendEmailChangeConfirmationNotice(new MailAddress(user.UnconfirmedEmailAddress, user.Username), confirmationUrl);

                TempData["Message"] = Strings.EmailUpdated_ConfirmationRequired;
            }
            else
            {
                TempData["Message"] = Strings.EmailUpdated;
            }

            return RedirectToAction(actionName: "Account", controllerName: "Users");
        }

        [HttpPost]
        [Authorize]
        public virtual async Task<ActionResult> CancelChangeEmail(AccountViewModel model)
        {
            var user = GetCurrentUser();

            if(string.IsNullOrWhiteSpace(user.UnconfirmedEmailAddress))
            {
                return RedirectToAction(actionName: "Account", controllerName: "Users");
            }

            await UserService.CancelChangeEmailAddress(user);

            TempData["Message"] = Strings.CancelEmailAddress;

            return RedirectToAction(actionName: "Account", controllerName: "Users");
        }


        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public virtual async Task<ActionResult> ChangePassword(AccountViewModel model)
        {
            var user = GetCurrentUser();

            var oldPassword = user.Credentials.FirstOrDefault(
                c => c.Type.StartsWith(CredentialTypes.Password.Prefix, StringComparison.OrdinalIgnoreCase));

            if (oldPassword == null)
            {
                // User is requesting a password set email
                await AuthService.GeneratePasswordResetToken(user, Constants.DefaultPasswordResetTokenExpirationHours * 60);

                return SendPasswordResetEmail(user, forgotPassword: false);
            }
            else
            {
                if (!ModelState.IsValidField("ChangePassword"))
                {
                    return await AccountView(model);
                }

                if (!(await AuthService.ChangePassword(user, model.ChangePassword.OldPassword, model.ChangePassword.NewPassword, model.ChangePassword.ResetApiKey)))
                {
                    ModelState.AddModelError("ChangePassword.OldPassword", Strings.CurrentPasswordIncorrect);
                    return await AccountView(model);
                }

                TempData["Message"] = Strings.PasswordChanged;

                return RedirectToAction("Account");
            }
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public virtual Task<ActionResult> RemovePassword()
        {
            var user = GetCurrentUser();
            var passwordCred = user.Credentials.SingleOrDefault(
                c => c.Type.StartsWith(CredentialTypes.Password.Prefix, StringComparison.OrdinalIgnoreCase));

            return RemoveCredential(user, passwordCred, Strings.PasswordRemoved);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public virtual Task<ActionResult> RemoveCredential(string credentialType)
        {
            var user = GetCurrentUser();
            var cred = user.Credentials.SingleOrDefault(
                c => String.Equals(c.Type, credentialType, StringComparison.OrdinalIgnoreCase));

            return RemoveCredential(user, cred, Strings.CredentialRemoved);
        }

        [HttpGet]
        public virtual ActionResult PasswordChanged()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<ActionResult> GenerateApiKey(int? expirationInDays)
        {
            // Get the user
            var user = GetCurrentUser();

            // Generate an API Key
            var apiKey = Guid.NewGuid();

            // Set expiration
            var expiration = TimeSpan.Zero;
            if ((await ConfigService.GetCurrent()).ExpirationInDaysForApiKeyV1 > 0)
            {
                expiration = TimeSpan.FromDays((await ConfigService.GetCurrent()).ExpirationInDaysForApiKeyV1);

                if (expirationInDays.HasValue && expirationInDays.Value > 0)
                {
                    expiration = TimeSpan.FromDays(Math.Min(expirationInDays.Value, (await ConfigService.GetCurrent()).ExpirationInDaysForApiKeyV1));
                }
            }

            // Add/Replace the API Key credential, and save to the database
            TempData["Message"] = Strings.ApiKeyReset;
            await AuthService.ReplaceCredential(user, 
                CredentialBuilder.CreateV1ApiKey(apiKey, expiration));

            return RedirectToAction("Account");
        }

        private async Task<ActionResult> RemoveCredential(User user, Credential cred, string message)
        {
            if (cred == null)
            {
                TempData["Message"] = Strings.NoCredentialToRemove;

                return RedirectToAction("Account");
            }

            // Count credentials and make sure the user can always login
            if (!String.Equals(cred.Type, CredentialTypes.ApiKeyV1, StringComparison.OrdinalIgnoreCase)
                && CountLoginCredentials(user) <= 1)
            {
                TempData["Message"] = Strings.CannotRemoveOnlyLoginCredential;
            }
            else
            {
                await AuthService.RemoveCredential(user, cred);

                // Notify the user of the change
                MessageService.SendCredentialRemovedNotice(user, cred);

                TempData["Message"] = message;
            }

            return RedirectToAction("Account");
        }

        private async Task<ActionResult> AccountView(AccountViewModel model)
        {
            // Load Credential info
            var user = GetCurrentUser();
            var curatedFeeds = CuratedFeedService.GetFeedsForManager(user.Key);
            var creds = user.Credentials.Select(c => AuthService.DescribeCredential(c)).ToList();

            model.Credentials = creds;
            model.CuratedFeeds = curatedFeeds.Select(f => f.Name);

            model.ExpirationInDaysForApiKeyV1 = (await ConfigService.GetCurrent()).ExpirationInDaysForApiKeyV1;

            return View("Account", model);
        }

        private static int CountLoginCredentials(User user)
        {
            return user.Credentials.Count(c =>
                c.Type.StartsWith(CredentialTypes.Password.Prefix, StringComparison.OrdinalIgnoreCase) ||
                c.Type.StartsWith(CredentialTypes.ExternalPrefix, StringComparison.OrdinalIgnoreCase));
        }

        private ActionResult SendPasswordResetEmail(User user, bool forgotPassword)
        {
            var resetPasswordUrl = Url.ConfirmationUrl(
                "ResetPassword",
                "Users",
                user.Username,
                user.PasswordResetToken,
                new { forgot = forgotPassword });
            MessageService.SendPasswordResetInstructions(user, resetPasswordUrl, forgotPassword);

            return RedirectToAction(actionName: "PasswordSent", controllerName: "Users");
        }
    }
}
