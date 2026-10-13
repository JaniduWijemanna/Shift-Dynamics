(() => {
  "use strict";

  const rolePaths = {
    Customer: "customer/dashboard.html",
    Manager: "manager/dashboard.html",
    Mechanic: "mechanic/dashboard.html",
    ServiceAdvisor: "advisor/dashboard.html",
    Storekeeper: "storekeeper/dashboard.html",
    Vendor: "vendor/dashboard.html"
  };

  function getValue(id, trim = true) {
    const value = document.getElementById(id)?.value || "";
    return trim ? value.trim() : value;
  }

  function getMessageElement(form) {
    return form.querySelector(
      "#loginMessage, #staffLoginMessage, #registerMessage, .sd-form-message"
    );
  }

  function showMessage(form, text, isError = false) {
    const element = getMessageElement(form);
    if (!element) return;

    element.textContent = text;
    element.classList.remove("sd-error", "sd-success");
    element.classList.add(isError ? "sd-error" : "sd-success");
    element.hidden = false;
  }

  function clearMessage(form) {
    const element = getMessageElement(form);
    if (!element) return;

    element.textContent = "";
    element.classList.remove("sd-error", "sd-success");
  }

  async function registerCustomer(form) {
    const fullName = getValue("fullName");
    const email = getValue("email");
    const phone = getValue("phone");
    const address = getValue("address");
    const password = getValue("password", false);
    const confirmPassword = getValue("confirmPassword", false);
    const terms = document.getElementById("terms");

    if (
      !fullName ||
      !email ||
      !phone ||
      !address ||
      !password ||
      !confirmPassword
    ) {
      throw new Error("Please complete all required fields.");
    }

    if (fullName.length < 2 || fullName.length > 150) {
      throw new Error("Full name must contain between 2 and 150 characters.");
    }

    if (
      email.length > 191 ||
      !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)
    ) {
      throw new Error("Please enter a valid email address.");
    }

    if (phone.length < 7 || phone.length > 30) {
      throw new Error("Phone number must contain between 7 and 30 characters.");
    }

    if (address.length > 500) {
      throw new Error("Address cannot exceed 500 characters.");
    }

    if (password.length < 8 || password.length > 100) {
      throw new Error("Password must contain between 8 and 100 characters.");
    }

    if (password !== confirmPassword) {
      throw new Error("Passwords do not match.");
    }

    if (terms && !terms.checked) {
      throw new Error("Please accept the terms and conditions.");
    }

    const data = await window.ShiftApi.request("/api/auth/register", {
      method: "POST",
      body: JSON.stringify({
        fullName,
        email,
        phone,
        address,
        password
      })
    });

    if (!data?.accessToken || data.role !== "Customer") {
      throw new Error(
        "Registration returned an unexpected response. Please try logging in."
      );
    }

    window.ShiftApi.save(data);
    showMessage(form, "Registration successful.");
    window.location.href = rolePaths.Customer;
  }

  async function login(form) {
    const isStaff = form.id === "staffLoginForm";

    const email = getValue(isStaff ? "staffEmail" : "loginEmail");
    const password = getValue(
      isStaff ? "staffPassword" : "loginPassword",
      false
    );

    if (!email || !password) {
      throw new Error("Please enter your email and password.");
    }

    const data = await window.ShiftApi.request("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({
        email,
        password
      })
    });

    if (!data?.accessToken) {
      throw new Error("Login returned an invalid response. Please try again.");
    }

    const destination = rolePaths[data.role];

    if (!destination) {
      throw new Error("This account does not have portal access.");
    }

    if (!isStaff && data.role !== "Customer") {
      throw new Error("Please use the staff portal for this account.");
    }

    if (isStaff && data.role === "Customer") {
      throw new Error("Please use the customer login for this account.");
    }

    window.ShiftApi.save(data);
    showMessage(form, "Login successful.");
    window.location.href = destination;
  }

  document.addEventListener(
    "submit",
    async (event) => {
      const form = event.target;

      if (!(form instanceof HTMLFormElement)) return;

      const supportedForms = [
        "loginForm",
        "staffLoginForm",
        "customerRegisterForm"
      ];

      if (!supportedForms.includes(form.id)) return;

      event.preventDefault();
      event.stopImmediatePropagation();

      if (form.dataset.apiSubmitting === "true") return;

      form.dataset.apiSubmitting = "true";

      const button = form.querySelector('button[type="submit"]');
      if (button) button.disabled = true;

      clearMessage(form);

      try {
        if (!window.ShiftApi) {
          throw new Error(
            "API client is missing. Load api-client.js before auth-integration.js."
          );
        }

        if (form.id === "customerRegisterForm") {
          await registerCustomer(form);
        } else {
          await login(form);
        }
      } catch (error) {
        showMessage(
          form,
          error.message || "Something went wrong. Please try again.",
          true
        );
      } finally {
        delete form.dataset.apiSubmitting;
        if (button) button.disabled = false;
      }
    },
    true
  );
})();