(() => {
 const auth=ShiftApi.auth(); const page=location.pathname;
 const text=(selector,value)=>{const e=document.querySelector(selector);if(e)e.textContent=value;};
 const state=(message,error=false)=>{let e=document.querySelector(".sd-api-status");if(!e){e=document.createElement("p");e.className="sd-api-status";document.querySelector(".sd-dashboard-content, main")?.prepend(e);}e.textContent=message;e.hidden=!message;e.style.color=error?"#b42318":"";};
 const live=(title,heads,rows)=>{let host=document.querySelector(".sd-api-live-data");if(!host){host=document.createElement("section");host.className="sd-table-card sd-api-live-data";document.querySelector(".sd-dashboard-content")?.prepend(host);}host.innerHTML=`<div class="sd-panel-header"><h3>${title}</h3></div><div class="sd-table-responsive"><table><thead><tr>${heads.map(x=>`<th>${x}</th>`).join("")}</tr></thead><tbody>${rows.length?rows.map(r=>`<tr>${r.map(c=>`<td>${c??"Ã¢â‚¬â€"}</td>`).join("")}</tr>`).join(""):`<tr><td colspan="${heads.length}">No records found.</td></tr>`}</tbody></table></div>`;};
 const guarded=role=>{if(!auth||auth.role!==role){ShiftApi.clear();location.href=page.includes("dashboard.html")?"../staff-login.html":"staff-login.html";return false;}return true;};
 async function manager(){
    if(!guarded("Manager")) return;

    const [summary, workshop, jobCards, mechanics, pendingVendors, draftInvoices] = await Promise.all([
        ShiftApi.request("/api/manager/summary"),
        ShiftApi.request("/api/manager/workshop"),
        ShiftApi.request("/api/manager/job-cards"),
        ShiftApi.request("/api/manager/mechanics"),
        ShiftApi.request("/api/vendors/registrations?status=Pending"),
        ShiftApi.request("/api/invoices?status=Draft")
    ]);

    document.body.dataset.apiManager = JSON.stringify({
        summary,
        workshop,
        jobCards,
        mechanics,
        pendingVendors,
        draftInvoices
    });

    /* =================================================
   REAL BILLING APPROVAL DATA
   ================================================= */

const billingInvoice = draftInvoices?.[0] ?? null;

const billingJob = billingInvoice
    ? jobCards.find(
        job => job.workOrderId === billingInvoice.workOrderId
      )
    : null;

const setBillingText = (id, value) => {
    const element = document.getElementById(id);
    if (element) {
        element.textContent = value ?? "—";
    }
};

const billingMoney = value =>
    `LKR ${Number(value ?? 0).toLocaleString("en-LK", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    })}`;

if (billingInvoice && billingJob) {
    setBillingText(
        "billingInvoiceNumber",
        `#${billingInvoice.invoiceNumber}`
    );

    setBillingText(
        "billingJobCardNumber",
        `#${billingJob.workOrderNumber}`
    );

    setBillingText(
        "billingCustomerName",
        billingJob.customerName
    );

    setBillingText(
        "billingVehicle",
        billingJob.vehicle
    );

    setBillingText(
        "billingLicensePlate",
        billingJob.registrationNumber
    );

    setBillingText(
        "billingService",
        billingJob.serviceName
    );

    /*
     * Billing amounts are bound to the existing card
     * without changing its visual structure.
     */
    const billingTextNodes = Array.from(
        document.querySelectorAll("body *")
    ).filter(
        element => element.children.length === 0
    );

    const replaceBillingAmount = (label, amount) => {
        const labelNode = billingTextNodes.find(
            element => element.textContent.trim() === label
        );

        if (!labelNode) return;

        const row = labelNode.closest("div");
        if (!row) return;

        const amountNode = Array.from(row.querySelectorAll("*"))
            .find(
                element =>
                    element.children.length === 0 &&
                    /^LKR\s/.test(element.textContent.trim())
            );

        if (amountNode) {
            amountNode.textContent = billingMoney(amount);
        }
    };

    replaceBillingAmount(
        "Workshop Labour",
        billingInvoice.laborCost
    );

    replaceBillingAmount(
        "Brake Fluid & Workshop Materials",
        billingInvoice.partsCost
    );

    const totalNode = billingTextNodes.find(
        element => element.textContent.trim() === "LKR 54,500"
    );

    if (totalNode) {
        totalNode.textContent =
            billingMoney(billingInvoice.totalAmount);
    }
}
const workOrderStatus = [
        "Open",
        "Assigned",
        "InProgress",
        "WaitingForParts",
        "Completed",
        "Cancelled"
    ];

    const bayStatus = [
        "Available",
        "Occupied",
        "Maintenance"
    ];

    const staffStatus = [
        "Active",
        "Inactive",
        "OnLeave"
    ];

    const statusName = (value, values) =>
        typeof value === "number"
            ? (values[value] ?? String(value))
            : String(value ?? "");

    /* Existing statistic cards â€” values only. */
    const cards = document.querySelectorAll(".sd-stat-card");

    const stats = [
        summary.activeJobCards,
        summary.mechanicsOnDuty,
        summary.vendorQuotes,
        summary.pendingApprovals
    ];

    cards.forEach((card, index) => {
        const value = card.querySelector("strong");
        if(value && stats[index] !== undefined){
            value.textContent = stats[index];
        }
    });

    /* Existing Workshop Bay Load list â€” data only. */
    const bayItems = document.querySelectorAll(".sd-bay-list .sd-bay-item");

    workshop.forEach((bay, index) => {
        const item = bayItems[index];
        if(!item) return;

        const name = item.querySelector("strong");
        const detail = item.querySelector(".sd-bay-info span, span:not(.sd-status)");
        const progress = item.querySelector(".sd-progress span");
        const status = item.querySelector(".sd-status");

        const currentBayStatus = statusName(bay.status, bayStatus);
        const currentJobStatus = bay.job
            ? statusName(bay.job.status, workOrderStatus)
            : "";

        if(name) name.textContent = bay.bayName;

        if(detail){
            detail.textContent = bay.job
                ? `${bay.job.vehicle} Â· ${bay.job.registrationNumber}`
                : currentBayStatus === "Maintenance"
                    ? "Maintenance"
                    : "Available";
        }

        const load = bay.job
            ? currentJobStatus === "Completed" ? 100
            : currentJobStatus === "InProgress" ? 75
            : 45
            : 0;

        if(progress) progress.style.width = `${load}%`;

        if(status){
            status.textContent = currentJobStatus || currentBayStatus;
        }
    });

    /* Existing Recent Job Cards table. */
    const recentBody = document.querySelector(".sd-table tbody");

    if(recentBody){
        recentBody.innerHTML = jobCards.slice(0,3).map(job => {
            const status = statusName(job.status, workOrderStatus);

            return `
                <tr>
                    <td><strong>#${job.workOrderNumber}</strong></td>
                    <td>${job.vehicle}</td>
                    <td>${job.serviceName}</td>
                    <td>
                        <span class="sd-status sd-status-progress">
                            ${status}
                        </span>
                    </td>
                </tr>
            `;
        }).join("");
    }

    /* Existing Job Card Review table. */
    const jobTable = document.getElementById("managerJobTable");

    if(jobTable){
        jobTable.innerHTML = jobCards.map(job => {
            const status = statusName(job.status, workOrderStatus);

            return `
                <tr
                    data-job-card="${job.workOrderNumber}"
                    data-vehicle="${job.vehicle}"
                    data-plate="${job.registrationNumber}"
                    data-service="${job.serviceName}"
                    data-priority=""
                    data-work-order-id="${job.workOrderId}"
                >
                    <td><strong>#${job.workOrderNumber}</strong></td>
                    <td>${job.vehicle}</td>
                    <td>${job.serviceName}</td>
                    <td>â€”</td>
                    <td>
                        <span class="sd-status sd-status-progress job-status">
                            ${status}
                        </span>
                    </td>
                    <td>
                        <button type="button" class="sd-review-btn">
                            Review
                        </button>
                    </td>
                </tr>
            `;
        }).join("");
    }

    /* Existing assignment dropdowns â€” real backend data. */
    const mechanicSelect = document.getElementById("mechanicSelect");
    const baySelect = document.getElementById("baySelect");

    if(mechanicSelect){
        mechanicSelect.innerHTML =
            '<option value="">Select mechanic</option>' +
            mechanics
                .filter(m => statusName(m.status, staffStatus) === "Active")
                .map(m =>
                    `<option value="${m.id}">${m.fullName}</option>`
                )
                .join("");
    }

    if(baySelect){
        baySelect.innerHTML =
            '<option value="">Select workshop bay</option>' +
            workshop
                .filter(b => statusName(b.status, bayStatus) === "Available")
                .map(b =>
                    `<option value="${b.bayId}">${b.bayName}</option>`
                )
                .join("");
    }
} async function mechanic(){if(!guarded("Mechanic"))return;const jobs=await ShiftApi.request("/api/mechanic/jobs");document.body.dataset.apiJobs=JSON.stringify(jobs);document.querySelectorAll(".sd-job-card, .sd-work-card").forEach((e,i)=>{const j=jobs[i];if(j)e.querySelector("h3, strong")?.replaceChildren(j.workOrderNumber);});live("My assigned jobs",["Job","Vehicle","Service","Status"],jobs.map(j=>[j.workOrderNumber,j.vehicle,j.service,j.status]));state(jobs.length?"Assigned jobs loaded.":"No assigned jobs.");document.addEventListener("click",async e=>{const b=e.target.closest("[data-start-timer],[data-stop-timer]");if(!b)return;const id=b.dataset.workOrderId;if(!id)return;await ShiftApi.request(b.hasAttribute("data-start-timer")?"/api/mechanic/timer/start":"/api/mechanic/timer/end",{method:"POST",body:JSON.stringify({workOrderId:id})});state("Timer updated.");});}
 async function inventory(){if(!guarded("Storekeeper"))return;const [items,requisitions]=await Promise.all([ShiftApi.request("/api/inventory"),ShiftApi.request("/api/inventory/requisitions")]);document.body.dataset.apiInventory=JSON.stringify({items,requisitions});live("Live inventory",["Part","On hand","Reorder level","Location"],items.map(i=>[i.name,i.onHandQty,i.reorderLevel,i.location]));state(`${items.length} inventory items and ${requisitions.length} requisitions loaded.`);document.addEventListener("click",async e=>{const b=e.target.closest("[data-review-requisition],[data-release-requisition]");if(!b)return;const id=b.dataset.requisitionId;if(!id)return;if(b.hasAttribute("data-release-requisition"))await ShiftApi.request(`/api/inventory/requisitions/${id}/release`,{method:"POST"});else await ShiftApi.request(`/api/inventory/requisitions/${id}/review`,{method:"POST",body:JSON.stringify({approve:b.dataset.approve!=="false",notes:""})});state("Requisition updated.");});}
 async function advisor(){if(!guarded("ServiceAdvisor"))return;const [customers,orders,estimates,services]=await Promise.all([ShiftApi.request("/api/customers"),ShiftApi.request("/api/work-orders"),ShiftApi.request("/api/estimates"),ShiftApi.request("/api/services")]);document.body.dataset.apiAdvisor=JSON.stringify({customers,orders,estimates,services});live("Live customers",["Customer","Email","Vehicles"],customers.map(c=>[`${c.firstName} ${c.lastName}`,c.email,c.vehicleCount]));state("Live customer, work-order, estimate, and service data loaded.");}
 async function vendor(){if(!guarded("Vendor"))return;const profile=await ShiftApi.request("/api/vendors/me");document.body.dataset.apiVendor=JSON.stringify(profile);text(".sd-topbar-profile strong",profile.businessName||profile.contactPerson);live("Vendor profile",["Business","Contact","Email","Status"],[[profile.businessName,profile.contactPerson,profile.email,profile.approvalStatus]]);state("Vendor profile loaded.");}
 const run=()=>{const f=page.includes("manager/")?manager():page.includes("mechanic/")?mechanic():page.includes("storekeeper/")?inventory():page.includes("advisor/")?advisor():page.includes("vendor/")?vendor():null;if(f)f.catch(e=>state(e.message,true));};run();
  window.addEventListener("shift:manager-refresh", () => {
    if (page.includes("manager/")) {
      manager().catch(e => state(e.message, true));
    }
  });
})();



