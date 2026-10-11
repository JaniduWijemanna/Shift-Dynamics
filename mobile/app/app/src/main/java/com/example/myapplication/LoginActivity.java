package com.example.myapplication;

import android.app.Activity;
import android.content.Intent;
import android.graphics.drawable.Drawable;
import android.os.Bundle;
import android.util.Patterns;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.ProgressBar;
import android.widget.TextView;

import org.json.JSONObject;

import java.io.InputStream;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class LoginActivity extends Activity {

    // Change this URL when the backend PC's IP address changes.
    private static final String API_BASE_URL =
            "http://192.168.8.141:5174";

    private int epoch = 0;

    private final ApiClient api = new ApiClient();
    private final ExecutorService worker =
            Executors.newSingleThreadExecutor();

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        Session.clear();
        showLogin();
    }

    private void goToDashboard() {
        Class<?> page = "ServiceAdvisor".equals(Session.role)
                ? AdvisorDashboardActivity.class
                : MechanicDashboardActivity.class;

        Intent next = new Intent(this, page);
        next.addFlags(
                Intent.FLAG_ACTIVITY_NEW_TASK
                        | Intent.FLAG_ACTIVITY_CLEAR_TASK
        );

        startActivity(next);
        finish();
    }

    private void loadLogo() {
        ImageView image = findViewById(R.id.logo);

        try (InputStream stream =
                     getAssets().open("brand-logo.png")) {
            Drawable logo = Drawable.createFromStream(
                    stream, "brand-logo"
            );

            if (logo != null) {
                image.setImageDrawable(logo);
            }
        } catch (Exception ignored) {
            // Keep the default logo defined in XML.
        }
    }

    private void showLogin() {
        epoch++;
        setContentView(R.layout.activity_login);
        loadLogo();

        EditText email = findViewById(R.id.email);
        EditText password = findViewById(R.id.password);
        TextView notice = findViewById(R.id.loginMessage);
        ProgressBar progress = findViewById(R.id.loginProgress);
        Button button = findViewById(R.id.login);

        email.setText(
                getPreferences(MODE_PRIVATE)
                        .getString("email", "")
        );

        button.setOnClickListener(view -> {
            try {
                String base = Domain.address(
                        API_BASE_URL, BuildConfig.DEBUG
                );

                String mail = email.getText()
                        .toString().trim();

                String pass = password.getText().toString();

                if (!Patterns.EMAIL_ADDRESS.matcher(mail).matches()
                        || pass.isEmpty()) {
                    throw new Exception(
                            "Enter your email and password."
                    );
                }

                button.setEnabled(false);
                progress.setVisibility(View.VISIBLE);
                notice.setText("");

                int ticket = epoch;

                worker.execute(() -> {
                    try {
                        JSONObject auth = (JSONObject) api.request(
                                base,
                                "",
                                "/api/auth/login",
                                "POST",
                                Domain.json(
                                        "email", mail,
                                        "password", pass
                                )
                        );

                        runOnUiThread(() -> {
                            if (isFinishing() || isDestroyed()
                                    || ticket != epoch) {
                                return;
                            }

                            try {
                                Session.login(auth, base);

                                getPreferences(MODE_PRIVATE)
                                        .edit()
                                        .putString("email", mail)
                                        .apply();

                                password.setText("");
                                goToDashboard();
                            } catch (Exception error) {
                                notice.setText(error.getMessage());
                                button.setEnabled(true);
                                progress.setVisibility(View.GONE);
                            }
                        });
                    } catch (Exception error) {
                        runOnUiThread(() -> {
                            if (isFinishing() || isDestroyed()
                                    || ticket != epoch) {
                                return;
                            }

                            notice.setText(error.getMessage());
                            button.setEnabled(true);
                            progress.setVisibility(View.GONE);
                        });
                    }
                });
            } catch (Exception error) {
                notice.setText(error.getMessage());
                button.setEnabled(true);
                progress.setVisibility(View.GONE);
            }
        });
    }

    @Override
    protected void onDestroy() {
        epoch++;
        worker.shutdown();
        super.onDestroy();
    }
}