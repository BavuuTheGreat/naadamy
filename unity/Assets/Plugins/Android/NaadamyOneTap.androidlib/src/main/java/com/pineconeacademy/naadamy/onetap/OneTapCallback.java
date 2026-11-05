package com.pineconeacademy.naadamy.onetap;

/** What OneTap.signIn answers; GoogleSignIn.cs implements it with an AndroidJavaProxy. */
public interface OneTapCallback {
    /** idToken: Google's ID token, or "" with `error`: "cancelled", "no-account", or "error: ..." with the details. */
    void onResult(String idToken, String error);
}
