package com.pineconeacademy.naadamy.onetap;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.NoCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.util.concurrent.Executor;

/**
 * "Sign in with Google" with the phone's own account picker (Credential Manager): one tap on an account already on the
 * phone, no browser. The ID token it gives is for `serverClientId` (the server's web client id) and carries `nonce`
 * (one the server gave out), so the server can check it (server/google-auth.js, act "native").
 */
public final class OneTap {
    private OneTap() { }

    /** Shows the picker; `callback` gets the token or why not, on a background thread. */
    public static void signIn(final Activity activity, final String serverClientId, final String nonce, final OneTapCallback callback) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    GetGoogleIdOption option = new GetGoogleIdOption.Builder()
                            .setFilterByAuthorizedAccounts(false) // every Google account on the phone, not only ones used here before
                            .setServerClientId(serverClientId)
                            .setNonce(nonce)
                            .build();
                    GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
                    Executor direct = new Executor() { @Override public void execute(Runnable r) { r.run(); } };
                    CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(), direct,
                            new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                                @Override public void onResult(GetCredentialResponse response) {
                                    Credential credential = response.getCredential();
                                    if (credential instanceof CustomCredential && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                                        try {
                                            callback.onResult(GoogleIdTokenCredential.createFrom(credential.getData()).getIdToken(), "");
                                        } catch (Exception e) {
                                            callback.onResult("", "error: " + e);
                                        }
                                    } else {
                                        callback.onResult("", "error: not a Google ID token");
                                    }
                                }
                                @Override public void onError(GetCredentialException e) {
                                    if (e instanceof GetCredentialCancellationException) callback.onResult("", "cancelled");
                                    else if (e instanceof NoCredentialException) callback.onResult("", "no-account");
                                    else callback.onResult("", "error: " + e.getClass().getSimpleName() + ": " + e.getMessage());
                                }
                            });
                } catch (Throwable t) {
                    callback.onResult("", "error: " + t);
                }
            }
        });
    }
}
