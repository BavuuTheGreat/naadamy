// Sign in with Apple (AppleSignIn.cs, server/apple-auth.js): Apple's own sheet, Face ID or the passcode, no browser.
// The request carries the SHA-256 of a nonce the server gave out; the identity token Apple returns goes to the server,
// which checks it. The callback runs on the main thread and gets the token, the given name (Apple shares it only the
// first time) and "" — or "", "" and why not: "cancelled", "unsupported", or "error: ...".
#import <AuthenticationServices/AuthenticationServices.h>
#import <UIKit/UIKit.h>

typedef void (*NaadamyAppleCallback)(const char* idToken, const char* givenName, const char* error);

API_AVAILABLE(ios(13.0))
@interface NaadamyAppleDelegate : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property (nonatomic, assign) NaadamyAppleCallback callback;
@property (nonatomic, strong) ASAuthorizationController* controller;
@end

static NSObject* naadamyAppleCurrent; // kept alive while Apple's sheet is up: the controller holds its delegate weakly

@implementation NaadamyAppleDelegate
- (void)finish:(NSString*)token name:(NSString*)name error:(NSString*)error
{
    NaadamyAppleCallback cb = self.callback;
    self.callback = NULL;
    if (cb) cb((token ?: @"").UTF8String, (name ?: @"").UTF8String, (error ?: @"").UTF8String);
    naadamyAppleCurrent = nil;
}
- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithAuthorization:(ASAuthorization*)authorization
{
    if ([authorization.credential isKindOfClass:[ASAuthorizationAppleIDCredential class]])
    {
        ASAuthorizationAppleIDCredential* credential = (ASAuthorizationAppleIDCredential*)authorization.credential;
        NSString* token = credential.identityToken ? [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding] : nil;
        if (token.length) [self finish:token name:credential.fullName.givenName error:nil];
        else [self finish:nil name:nil error:@"error: no identity token"];
    }
    else [self finish:nil name:nil error:@"error: not an Apple ID"];
}
- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithError:(NSError*)error
{
    if (error.code == ASAuthorizationErrorCanceled) [self finish:nil name:nil error:@"cancelled"];
    else [self finish:nil name:nil error:[NSString stringWithFormat:@"error: %ld %@", (long)error.code, error.localizedDescription]];
}
- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController*)controller
{
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
        if ([scene isKindOfClass:[UIWindowScene class]])
            for (UIWindow* window in ((UIWindowScene*)scene).windows)
                if (window.isKeyWindow) return window;
    return UIApplication.sharedApplication.windows.firstObject;
}
@end

extern "C" void NaadamyAppleSignIn(const char* hashedNonce, NaadamyAppleCallback callback)
{
    if (@available(iOS 13.0, *))
    {
        ASAuthorizationAppleIDRequest* request = [[[ASAuthorizationAppleIDProvider alloc] init] createRequest];
        request.requestedScopes = @[ASAuthorizationScopeFullName, ASAuthorizationScopeEmail];
        request.nonce = [NSString stringWithUTF8String:hashedNonce ?: ""];
        NaadamyAppleDelegate* d = [NaadamyAppleDelegate new];
        d.callback = callback;
        d.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
        d.controller.delegate = d;
        d.controller.presentationContextProvider = d;
        naadamyAppleCurrent = d;
        [d.controller performRequests];
    }
    else if (callback) callback("", "", "unsupported");
}
