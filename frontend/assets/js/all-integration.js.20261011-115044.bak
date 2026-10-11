(() => {
  "use strict";
  if (!window.ShiftApi) return;
  const api = window.ShiftApi;
  const esc = value => String(value ?? "").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
  const money = v => new Intl.NumberFormat("en-LK",{style:"currency",currency:"LKR"}).format(Number(v)||0);
  const date = v => v && !Number.isNaN(new Date(v).getTime()) ? new Date(v).toLocaleString("en-GB") : "—";
  const enums = {
    job:["Open","Assigned","InProgress","Waiting for parts","Completed","Cancelled"],
    estimate:["Draft","Sent","Approved","Rejected","Expired"],
    invoice:["Draft","Issued","Partially paid","Paid","Overdue","Cancelled"],
    appointment:["Scheduled","Confirmed","Completed","Cancelled","No show"],
    modification:["Submitted","Under review","Quoted","Approved","Rejected","Cancelled"],
    emergency:["Pending","Accepted","In progress","Completed","Cancelled"],
    requisition:["Pending","Approved","Rejected","Released","Cancelled"],
    rfq:["Open","Closed","Awarded","Cancelled"], quote:["Submitted","Withdrawn","Rejected","Accepted"],
    order:["Draft","Approved","Sent","Partially received","Received","Cancelled"],
    vendor:["Pending","Approved","Rejected"],role:["Customer","Service advisor","Manager","Mechanic","Storekeeper","Vendor","Admin"]
  };
  const enumIndex = (type,v) => /^\d+$/.test(String(v)) ? Number(v) : enums[type].findIndex(x=>x.replace(/\s/g,"").toLowerCase()===String(v).replace(/\s/g,"").toLowerCase());
  const status = (type,v) => enums[type][enumIndex(type,v)] || String(v??"Unknown");
  async function request(path, method="GET", body) {
    const auth=api.auth(); const headers={"Accept":"application/json"};
    if(auth?.accessToken) headers.Authorization=`Bearer ${auth.accessToken}`;
    if(body!==undefined && !(body instanceof FormData)) headers["Content-Type"]="application/json";
    const response=await fetch(`${api.base}${path}`,{method,headers,body:body===undefined?undefined:(body instanceof FormData?body:JSON.stringify(body))});
    let payload=null; try {payload=await response.json();} catch {}
    if(!response.ok) {
      if(response.status===401) {api.clear();throw new Error("Your session expired. Please log in again.");}
      const details=payload?.errors?Object.values(payload.errors).flat().join(" "):"";
      throw new Error(details||payload?.message||payload?.title||`Request failed (${response.status}).`);
    }
    return payload?.data ?? payload;
  }
  async function list(path) {
    let page=1, result=[];
    while(true) {
      const data=await request(path+(path.includes("?")?"&":"?")+`page=${page}&pageSize=100`);
      if(Array.isArray(data)) return data;
      if(!Array.isArray(data?.items)) throw new Error("The list response could not be read.");
      result.push(...data.items);
      if(!data.items.length||result.length>=data.totalCount||data.items.length<100)return result;
      page++;
    }
  }
  let currentReload=()=>{};
  function notice(root,text,error=false) {
    root=root._host || root;
    let box=root.querySelector(".all-message");
    if(!box){box=document.createElement("p");box.className="all-message";box.setAttribute("role","status");root.prepend(box);}
    box.textContent=text;box.style.cssText=`display:block;padding:12px;border-radius:8px;color:${error?"#b91c1c":"#166534"};background:${error?"#fef2f2":"#f0fdf4"}`;
  }
  function action(root,label,handler,parent=root) {
    const b=document.createElement("button");b.type="button";b.className="sd-secondary-button";b.textContent=label;b.style.margin="4px";
    b.onclick=async()=>{if(b.disabled)return;b.disabled=true;try{await handler();}catch(e){notice(root,e.message,true);}finally{b.disabled=false;}};
    parent.appendChild(b);return b;
  }
  async function mutate(root,path,method="POST",body) {await request(path,method,body);await (root._reload || currentReload)();notice(root,"Saved successfully.");}
  function table(root,title,rows,columns,buttons) {
    const card=document.createElement("article");card.className="sd-dashboard-card all-card";
    const h=document.createElement("h3");h.textContent=title;card.append(h);root.append(card);
    if(!rows.length){const p=document.createElement("p");p.textContent="No records available.";card.append(p);return;}
    const wrap=document.createElement("div");wrap.style.overflowX="auto";
    const t=document.createElement("table");t.style.cssText="width:100%;border-collapse:collapse;font-size:14px";
    t.innerHTML=`<thead><tr>${columns.map(c=>`<th>${esc(c[0])}</th>`).join("")}${buttons?"<th>Actions</th>":""}</tr></thead>`;
    const tbody=document.createElement("tbody");t.append(tbody);
    rows.forEach(row=>{
      const tr=document.createElement("tr");
      columns.forEach(c=>{const td=document.createElement("td");const value=typeof c[1]==="function"?c[1](row):row[c[1]];if(value instanceof Node)td.append(value);else td.textContent=value??"—";tr.append(td);});
      if(buttons){const td=document.createElement("td");buttons(row,(label,fn)=>action(root,label,fn,td));tr.append(td);}
      tbody.append(tr);
    });
    wrap.append(t);card.append(wrap);
  }
  const field=(name,label,type="text",extra={})=>({name,label,type,...extra});
  const select=(name,label,items,getLabel,optional=false)=>field(name,label,"select",{items,getLabel,optional});
  function form(root,title,fields,submit,label="Save") {
    const card=document.createElement("article");card.className="sd-dashboard-card all-card";
    const heading=document.createElement("h3");heading.textContent=title;card.append(heading);
    const f=document.createElement("form");const grid=document.createElement("div");grid.className="sd-form-grid";
    for(const spec of fields){
      const group=document.createElement("div");group.className="sd-form-group";
      const lab=document.createElement("label");lab.textContent=spec.label;
      const id=`all-${spec.name}-${Math.random().toString(36).slice(2)}`;lab.htmlFor=id;
      const input=document.createElement(spec.type==="select"?"select":spec.type==="textarea"?"textarea":"input");
      input.id=id;input.name=spec.name;input.required=!spec.optional;
      if(spec.type==="select"){
        input.add(new Option(spec.optional?"None":"Select an option",""));
        spec.items.forEach(item=>input.add(new Option(spec.getLabel(item),item.id)));
      }else if(spec.type==="textarea")input.rows=3;else input.type=spec.type;
      if(spec.min!==undefined)input.min=spec.min;if(spec.max!==undefined)input.max=spec.max;
      if(spec.maxLength)input.maxLength=spec.maxLength;
      if(spec.type==="number")input.step=spec.step||"0.01";
      if(spec.value!==undefined)input.value=spec.value;
      input.style.cssText="width:100%;padding:10px;border:1px solid #cbd5e1;border-radius:8px;background:#fff;color:#0f172a";
      group.append(lab,input);grid.append(group);
    }
    const button=document.createElement("button");button.type="submit";button.className="sd-primary-button";button.textContent=label;
    f.append(grid,button);card.append(f);root.append(card);
    f.onsubmit=async event=>{
      event.preventDefault();event.stopImmediatePropagation();if(button.disabled)return;if(!f.reportValidity())return;
      const data=Object.fromEntries(new FormData(f));
      for(const spec of fields){if(spec.type==="number")data[spec.name]=data[spec.name]===""?null:Number(data[spec.name]);
        else if(spec.type==="datetime-local")data[spec.name]=data[spec.name]?new Date(data[spec.name]).toISOString():null;
        else if(spec.optional && data[spec.name]==="")data[spec.name]=null;}
      button.disabled=true;try{await submit(data);await (root._reload || currentReload)();notice(root,"Saved successfully.");}catch(e){notice(root,e.message,true);}finally{button.disabled=false;}
    };
  }
  function replaceSection(id) {
    const section=document.getElementById(id);if(!section)return null;
    Array.from(section.children).forEach(el=>{if(!el.classList.contains("sd-section-heading"))el.remove();});
    const root=document.createElement("div");root.className="all-module";section.append(root);return root;
  }
  const jobs = () => list("/api/work-orders");
  const jobLabel = j => `${j.workOrderNumber} • ${j.vehicleReg||j.vehicle||""}`;
  const estimates = () => list("/api/estimates");
  const invoices = () => list("/api/invoices");
  const partLabel=p=>`${p.partNumber} • ${p.name}`;
  const numericOptions=names=>names.map((name,id)=>({id,name}));
  const priceFields=[field("laborCost","Labor cost (LKR)","number",{min:0,value:0}),field("partsCost","Parts cost (LKR)","number",{min:0,value:0}),field("taxAmount","Tax (LKR)","number",{min:0,value:0}),field("discountAmount","Discount (LKR)","number",{min:0,value:0}),field("notes","Notes","textarea",{optional:true,maxLength:1000})];

  async function showEstimates(root,role) {
    const data=await estimates();
    table(root,"Estimates",data,[["Estimate","estimateNumber"],["Total",r=>money(r.totalAmount)],["Status",r=>status("estimate",r.status)],["Notes","notes"]],(r,button)=>{
      if(role==="Customer" && enumIndex("estimate",r.status)===1){
        button("Approve",()=>mutate(root,`/api/estimates/${r.id}/decision`,"POST",{approve:true,comment:null}));
        button("Request changes",async()=>{const comment=prompt("Describe the changes required:");if(comment===null)return;await mutate(root,`/api/estimates/${r.id}/decision`,"POST",{approve:false,comment});});
      }else if(role!=="Customer" && enumIndex("estimate",r.status)===0)button("Send to customer",()=>mutate(root,`/api/estimates/${r.id}/send`));
    });
    if(role!=="Customer")form(root,"Create estimate",[select("workOrderId","Job card",await jobs(),jobLabel),...priceFields],d=>request("/api/estimates","POST",d),"Create estimate");
  }
  async function showInvoices(root,role) {
    const data=await invoices();
    table(root,"Invoices",data,[["Invoice","invoiceNumber"],["Total",r=>money(r.totalAmount)],["Paid",r=>money(r.amountPaid)],["Balance",r=>money(r.balanceDue)],["Status",r=>status("invoice",r.status)]],(r,button)=>{
      if(["Manager","Admin"].includes(role)&&enumIndex("invoice",r.status)===0)button("Approve and issue",()=>mutate(root,`/api/invoices/${r.id}/approve`));
    });
    if(role==="ServiceAdvisor")form(root,"Create invoice",[select("workOrderId","Job card",await jobs(),jobLabel),select("estimateId","Approved estimate (optional)",(await estimates()).filter(e=>enumIndex("estimate",e.status)===2),e=>e.estimateNumber,true),...priceFields],d=>request("/api/invoices","POST",d),"Create invoice");
    if(role==="Customer"){
      const p=document.createElement("p");p.textContent="Record a test payment below. This records a payment in the system; it does not charge a bank card.";root.append(p);
      const payable=data.filter(i=>[1,2,4].includes(enumIndex("invoice",i.status))&&Number(i.balanceDue)>0);
      if(payable.length)form(root,"Record test payment",[select("invoiceId","Invoice",payable,i=>`${i.invoiceNumber} • Balance ${money(i.balanceDue)}`),field("amount","Amount (LKR)","number",{min:0.01}),select("method","Method",numericOptions(["Cash","Card","Bank transfer","Online"]),r=>r.name),field("transactionReference","Reference","text",{optional:true}),field("notes","Notes","textarea",{optional:true,maxLength:1000})],d=>request("/api/payments","POST",{...d,method:Number(d.method)}),"Record test payment");
      table(root,"Payment history",await list("/api/payments"),[["Invoice",r=>r.invoice?.invoiceNumber||"—"],["Amount",r=>money(r.amount)],["Date",r=>date(r.paymentDate)],["Reference","transactionReference"]]);
    }
  }
  async function showModifications(root,role) {
    // SD_MODIFICATION_JOB_LINKS
    const modificationJobs=role==="Customer"
      ? []
      : await list("/api/modification-jobs");
    table(root,"Modification requests",await list("/api/modification-requests"),[["Type","requestType"],["Description","description"],["Status",r=>status("modification",r.status)],["Proposed cost",r=>r.proposedCost==null?"—":money(r.proposedCost)],["Advisor notes","advisorNotes"]],role==="Customer"?(r,b)=>{if(enumIndex("modification",r.status)===2){b("Accept quotation",()=>mutate(root,`/api/modification-requests/${r.id}/decision`,"POST",{approve:true}));b("Decline quotation",()=>mutate(root,`/api/modification-requests/${r.id}/decision`,"POST",{approve:false}));}}:(r,b)=>{
            // SD_MODIFICATION_JOB_BUTTON
      if(enumIndex("modification",r.status)===3) {
        const number="WO-MOD-"+r.id.replace(/-/g,"").toLowerCase();
        const linked=modificationJobs.find(
          j=>j.workOrderNumber===number
        );

        if(linked) {
          b("Job created",()=>objectCard(root,"Linked job",{
            job:linked.workOrderNumber,
            status:status("job",linked.status),
            nextStep:"Manager assigns this job in Assignments."
          }));
        } else {
          b("Create job card",async()=>{
            const services=await list("/api/services");

            if(!services.length) {
              throw new Error("Create an active service package first.");
            }

            root.querySelector(".all-modification-job-form")?.remove();

            const pane=document.createElement("section");
            pane.className="all-modification-job-form";
            pane._host=root;
            pane._reload=root._reload;

            const info=document.createElement("p");
            info.textContent=
              "Vehicle: "+(r.vehicle?.registrationNumber||r.vehicleId)+
              " | Accepted price: "+money(r.proposedCost)+
              " | Modification: "+r.requestType;

            pane.append(info);
            root.append(pane);

            form(
              pane,
              "Create job for approved modification",
              [
                select(
                  "serviceId",
                  "Service package",
                  services,
                  s=>s.name
                )
              ],
              d=>request(
                `/api/modification-jobs/${r.id}`,
                "POST",
                {serviceId:d.serviceId}
              ),
              "Create job card"
            );

            pane.scrollIntoView({
              behavior:"smooth",
              block:"start"
            });
          });
        }

        return;
      }

      if([3,4,5].includes(enumIndex("modification",r.status)))return;
      b("Review",async()=>{
        const notes=prompt("Advisor notes:",r.advisorNotes||"");if(notes===null)return;
        const amount=prompt("Proposed cost in LKR (leave blank if not quoted):",r.proposedCost??"");if(amount===null)return;
        if(amount!==""&&(!Number.isFinite(Number(amount))||Number(amount)<0))throw new Error("Enter a valid cost.");
        await mutate(root,`/api/modification-requests/${r.id}/review`,"PATCH",{status:amount===""?1:2,proposedCost:amount===""?null:Number(amount),advisorNotes:notes});
      });
      b("Reject",()=>mutate(root,`/api/modification-requests/${r.id}/review`,"PATCH",{status:4,proposedCost:null,advisorNotes:"Request rejected"}));
    });
    if(role==="Customer")form(root,"Request a modification",[select("vehicleId","Vehicle",await list("/api/vehicles"),v=>`${v.registrationNumber} • ${v.make} ${v.model}`),field("requestType","Modification type","text",{maxLength:100}),field("description","Describe your request","textarea",{maxLength:2000})],d=>request("/api/modification-requests","POST",d),"Submit request");
  }
  async function showEmergency(root,role) {
    table(root,"Emergency requests",await list("/api/integration/emergencies"),[["Customer","customerName"],["Vehicle","vehicleRegistration"],["Location","location"],["Problem","problemDescription"],["Status",r=>status("emergency",r.status)],["Requested",r=>date(r.requestedAt)]],role==="Customer"?null:(r,b)=>{
      if([3,4].includes(enumIndex("emergency",r.status)))return;
      for(const [label,value] of [["Accept",1],["In progress",2],["Complete",3],["Cancel",4]])b(label,()=>mutate(root,`/api/integration/emergencies/${r.id}`,"PATCH",{status:value}));
    });
    if(role==="Customer"){
      const auth=api.auth();
      form(root,"Request emergency assistance",[select("vehicleId","Vehicle (optional)",await list("/api/vehicles"),v=>`${v.registrationNumber} • ${v.make} ${v.model}`,true),field("location","Location / address"),field("problemDescription","Describe the problem","textarea"),field("contactName","Contact name","text",{value:auth.fullName}),field("contactPhone","Contact phone","tel",{value:auth.phone})],d=>request("/api/emergency/requests","POST",d),"Submit emergency request");
    }
  }
  async function showNotifications(root) {
    table(root,"Notifications",await list("/api/notifications"),[["Title","title"],["Message","body"],["Date",r=>date(r.createdAt)],["Read",r=>r.isRead?"Yes":"No"]],(r,b)=>{if(!r.isRead)b("Mark read",()=>mutate(root,`/api/notifications/${r.id}/read`,"PATCH"));});
  }
  async function showHistory(root) {
    const records = await list("/api/service-history");

    if (!records.length) {
      table(root, "Completed services", [], []);
      return;
    }

    for (const record of records) {
      const card = document.createElement("article");
      card.className = "sd-dashboard-card all-card";

      const heading = document.createElement("h3");
      heading.textContent = record.workOrderNumber + " - " +
        (record.service?.name || "Workshop service");
      card.append(heading);

      const info = document.createElement("p");
      info.textContent = "Vehicle: " +
        (record.vehicle?.registrationNumber || "Not recorded") +
        " | Completed: " + date(record.completedAt);
      card.append(info);

      for (const [label, value] of [
        ["Service details", record.description],
        ["Technician notes", record.technicianNotes]
      ]) {
        if (!value) continue;

        const paragraph = document.createElement("p");
        const title = document.createElement("strong");
        title.textContent = label + ": ";

        paragraph.append(title, document.createTextNode(value));
        card.append(paragraph);
      }

      const details = document.createElement("details");
      const summary = document.createElement("summary");

      summary.textContent =
        "View diagnostic findings, repairs and recommendations";

      summary.style.cssText =
        "cursor:pointer;font-weight:600;padding:12px 0;color:#c2410c";

      details.append(summary);

      table(details, "Diagnostic findings", record.diagnostics || [], [
        ["Finding", "finding"],
        ["Severity", "severity"],
        ["Date", item => date(item.createdAt)]
      ]);

      table(details, "Repair actions", record.repairs || [], [
        ["Action", "action"],
        ["Notes", "notes"],
        ["Date", item => date(item.createdAt)]
      ]);

      table(details, "Mechanic recommendations",
        record.recommendations || [], [
          ["Recommendation", "recommendation"],
          ["Priority", "priority"],
          ["Date", item => date(item.createdAt)]
        ]
      );

      card.append(details);
      root.append(card);
    }
  }
  async function showTracker(root) {
    const data=(await jobs()).filter(j=>![4,5].includes(enumIndex("job",j.status)));
    table(root,"Active workshop jobs",data,[["Job","workOrderNumber"],["Vehicle","vehicleReg"],["Service","serviceName"],["Status",r=>status("job",r.status)],["Created",r=>date(r.createdAt)],["Started",r=>date(r.startedAt)]]);
    if(!data.length){const p=document.createElement("p");p.textContent="A job appears after the advisor creates a job card. Completed jobs appear in Service History.";root.append(p);}
  }
  async function showAppointments(root) {
    const appointments=await list("/api/integration/appointments");
    table(root,"Appointments",appointments,[["Customer","customerName"],["Vehicle","vehicleRegistration"],["Service","serviceType"],["Date",r=>date(r.appointmentDate)],["Status",r=>status("appointment",r.status)]],(r,b)=>{
      if(enumIndex("appointment",r.status)===0)b("Confirm",()=>mutate(root,`/api/appointments/${r.id}/confirm`));
      if(!r.hasJob && [0,1].includes(enumIndex("appointment",r.status)))b("Create job",async()=>{
        const service=(await list("/api/services")).find(s=>s.name.toLowerCase()===r.serviceType.toLowerCase());
        if(!service)throw new Error("Service package not found. Create the job from Job Cards and select a package.");
        await mutate(root,"/api/work-orders","POST",{customerId:r.customerId,vehicleId:r.vehicleId,serviceId:service.id,appointmentId:r.id,description:r.notes||r.serviceType});
      });
    });
  }
  async function showCustomers(root) {
    const customers=await list("/api/customers");
    table(root,"Customers",customers,[["Name",c=>`${c.firstName} ${c.lastName}`],["Email","email"],["Phone","phone"],["Address","address"],["Vehicles","vehicleCount"]]);
    form(root,"Register customer",[field("fullName","Full name"),field("email","Email","email"),field("phone","Phone","tel"),field("address","Address","text",{optional:true}),field("password","Initial password","password",{maxLength:100})],d=>request("/api/integration/customers","POST",d),"Register customer");
    form(root,"Add customer vehicle",[select("customerId","Customer",customers,c=>`${c.firstName} ${c.lastName} • ${c.email}`),field("registrationNumber","Registration number"),field("make","Make"),field("model","Model"),field("year","Year","number",{min:1900,max:2100,step:"1"}),field("vin","VIN","text",{optional:true})],d=>request("/api/integration/vehicles","POST",d),"Add vehicle");
  }
  async function showJobs(root,role) {
    const data=role==="Mechanic"?await list("/api/mechanic/jobs"):await jobs();
    table(root,"Job cards",data,[["Job","workOrderNumber"],["Customer",r=>r.customerName||r.customer],["Vehicle",r=>r.vehicleReg||r.vehicle],["Service",r=>r.serviceName||r.service],["Status",r=>status("job",r.status)]],role==="Mechanic"?(r,b)=>{
      const id=r.workOrderId; const state=enumIndex("job",r.status);
      if([4,5].includes(enumIndex("job",r.status)))return;
      if(!r.hasActiveTimer)b("Start timer",()=>mutate(root,"/api/mechanic/timer/start","POST",{workOrderId:id}));
      if(r.hasActiveTimer)b("Stop timer",()=>mutate(root,"/api/mechanic/timer/end","POST",{workOrderId:id}));
      if(state!==2)b("Start work",()=>mutate(root,`/api/mechanic/jobs/${id}/status`,"PATCH",{status:2,notes:null}));
      if(state===2)b("Waiting for parts",()=>mutate(root,`/api/mechanic/jobs/${id}/status`,"PATCH",{status:3,notes:null}));
      if(state===2)b("Complete job",async()=>{if(confirm("Mark this job completed?"))await mutate(root,`/api/mechanic/jobs/${id}/complete`);});
      b("Details",async()=>{const details=await request(`/api/mechanic/jobs/${id}`);const pane=document.createElement("section");root.querySelector(".all-job-details")?.remove();pane.className="all-job-details";root.append(pane);
        const w=details.workOrder;objectCard(pane,"Job details",{job:w.workOrderNumber,vehicle:w.vehicle?.registrationNumber,customer:w.customer?.name,service:w.service?.name,status:status("job",w.status),description:w.description,technicianNotes:w.technicianNotes});
        table(pane,"Diagnostic findings",details.diagnostics||[],[["Finding","finding"],["Severity","severity"],["Date",x=>date(x.createdAt)]]);
        table(pane,"Repair actions",details.repairs||[],[["Action","action"],["Notes","notes"],["Date",x=>date(x.createdAt)]]);
        table(pane,"Recommendations",details.recommendations||[],[["Recommendation","recommendation"],["Priority","priority"]]);});
    }:null);
    if(role==="ServiceAdvisor"){
      const [customers,vehicles,services]=await Promise.all([list("/api/customers"),list("/api/vehicles"),list("/api/services")]);
      form(root,"Create job card",[select("customerId","Customer",customers,c=>`${c.firstName} ${c.lastName}`),select("vehicleId","Vehicle",vehicles,v=>`${v.registrationNumber} • ${v.make} ${v.model}`),select("serviceId","Service",services,s=>s.name),field("description","Requirements / notes","textarea",{optional:true})],d=>request("/api/work-orders","POST",{...d,appointmentId:null}),"Create job");
    }
    if(role==="Mechanic"){
      const active=data.filter(j=>![4,5].includes(enumIndex("job",j.status))).map(j=>({...j,id:j.workOrderId}));
      const selector=()=>select("workOrderId","Assigned job",active,jobLabel);
      for(const [title,path,name,label] of [["Diagnostic finding","diagnostics","finding","Finding"],["Repair action","repairs","action","Action performed"],["Recommendation","recommendations","recommendation","Recommendation"]])
        form(root,title,[selector(),field(name,label,"textarea"),field(path==="diagnostics"?"severity":path==="recommendations"?"priority":"notes",path==="diagnostics"?"Severity":path==="recommendations"?"Priority":"Notes","textarea",{optional:true})],d=>request(`/api/mechanic/${path}`,"POST",d),"Save");
      form(root,"Request spare parts",[selector(),select("partId","Part",await list("/api/integration/parts"),partLabel),field("partSpec","Part specification"),field("qtyRequested","Quantity","number",{min:1,step:"1"}),select("urgency","Urgency",numericOptions(["Low","Normal","High","Critical"]),r=>r.name),field("reason","Reason","textarea",{optional:true})],d=>request("/api/mechanic/requisitions","POST",{...d,urgency:Number(d.urgency)}),"Request parts");
    }
  }
  async function showAssignments(root) {
    const [j,m,b]=await Promise.all([jobs(),list("/api/manager/mechanics"),list("/api/manager/bays")]);
    table(root,"Mechanics",m,[["Name","fullName"],["Employee number","employeeNumber"],["Active jobs","activeJobs"]]);
    form(root,"Assign mechanic",[select("workOrderId","Job card",j.filter(x=>![4,5].includes(enumIndex("job",x.status))),jobLabel),select("mechanicStaffId","Mechanic",m.filter(x=>x.activeJobs===0),x=>x.fullName),select("bayId","Bay (optional)",b.filter(x=>x.status===0||x.status==="Available"),x=>x.name,true)],d=>request("/api/manager/assignments","POST",d),"Assign job");
    table(root,"Workshop bays",await list("/api/manager/workshop"),[["Bay","bayName"],["Job",r=>r.job?.workOrderNumber||"—"],["Mechanic",r=>r.job?.mechanicName||"—"]]);
  }
  function partImage(url,name) {
    if(!url || !/^\/uploads\/parts\/[a-f0-9]{32}\.(jpg|png|webp)$/i.test(url))return "No image";
    const image=document.createElement("img");image.src=api.base+url;image.alt=name;image.loading="lazy";
    image.style.cssText="width:72px;height:72px;object-fit:cover;border-radius:8px";return image;
  }
  async function showStock(root) {
    const inventory = await list("/api/inventory");

    table(root, "Inventory", inventory, [
      ["Part", "name"],
      ["Number", "partNumber"],
      ["On hand", "onHandQty"],
      ["Reserved", "reservedQty"],
      ["Reorder level", "reorderLevel"],
      ["Cost", row => money(row.unitCost)],
      ["Location", "location"]
    ]);

    form(root, "Add part and opening stock", [
      field("partNumber", "Part number"),
      field("name", "Part name"),
      field("category", "Category", "text", { optional: true }),
      field("description", "Description", "textarea", { optional: true }),
      field("compatibility", "Compatibility", "text", { optional: true }),
      field("onHandQty", "Opening quantity", "number", {
        min: 0, value: 0, step: "1"
      }),
      field("reorderLevel", "Reorder level", "number", {
        min: 0, value: 0, step: "1"
      }),
      field("unitCost", "Unit cost (LKR)", "number", {
        min: 0, value: 0
      }),
      field("location", "Storage location", "text", { optional: true })
    ], data => request("/api/inventory/parts", "POST", data), "Add part");
  }

  async function showRequisitions(root) {
    table(root,"Parts requests",await list("/api/inventory/requisitions"),[["Specification","partSpec"],["Requested","qtyRequested"],["Released","qtyReleased"],["Status",r=>status("requisition",r.status)],["Reason","reason"]],(r,b)=>{
      if(enumIndex("requisition",r.status)===0){b("Approve",()=>mutate(root,`/api/inventory/requisitions/${r.id}/review`,"POST",{approve:true,notes:null}));b("Reject",()=>mutate(root,`/api/inventory/requisitions/${r.id}/review`,"POST",{approve:false,notes:null}));}
      if(enumIndex("requisition",r.status)===1)b("Release stock",()=>mutate(root,`/api/inventory/requisitions/${r.id}/release`));
    });
  }
  async function showRFQ(root,role) {
    const data=await list(role==="Vendor"?"/api/procurement/vendor/quote-requests":"/api/procurement/quote-requests");
    table(root,"Quotation requests",data,[["Request","requestNumber"],["Part","partDescription"],["Quantity","quantity"],["Required by",r=>date(r.requiredBy)],["Status",r=>status("rfq",r.status)]]);
    if(role==="Storekeeper")form(root,"Request vendor quotations",[select("partId","Part",await list("/api/integration/parts"),partLabel),field("partDescription","Description"),field("quantity","Quantity","number",{min:1,step:"1"}),field("requiredBy","Required by","datetime-local")],d=>request("/api/procurement/quote-requests","POST",{...d,partRequisitionId:null}),"Publish request");
    if(role==="Vendor")form(root,"Submit quotation",[select("requestId","Quotation request",data.filter(r=>enumIndex("rfq",r.status)===0),r=>`${r.requestNumber} • ${r.partDescription}`),field("unitPrice","Unit price (LKR)","number",{min:0.01}),field("availableQuantity","Available quantity","number",{min:1,step:"1"}),field("deliveryDays","Delivery days","number",{min:0,max:365,step:"1"}),field("notes","Notes","textarea",{optional:true,maxLength:1000})],d=>{const {requestId,...body}=d;return request(`/api/procurement/quote-requests/${requestId}/quotes`,"POST",body);},"Submit quotation");
  }
  async function showQuotes(root,role) {
    table(root,"Vendor quotations",await list("/api/integration/quotes"),[["Request","requestNumber"],["Vendor","businessName"],["Part","partDescription"],["Unit price",r=>money(r.unitPrice)],["Quantity","availableQuantity"],["Delivery days","deliveryDays"],["Status",r=>status("quote",r.status)]],role==="Vendor"?null:(r,b)=>{if(enumIndex("quote",r.status)===0)b("Award quotation",()=>mutate(root,`/api/procurement/quotes/${r.id}/award`));});
  }
  async function showOrders(root,role) {
    table(root,"Purchase orders",await list("/api/integration/purchase-orders"),[["Order","purchaseOrderNumber"],["Vendor","businessName"],["Part","partDescription"],["Quantity","quantity"],["Received","receivedQuantity"],["Status",r=>status("order",r.status)],["Expected",r=>date(r.expectedDeliveryAt)]],(r,b)=>{
      if([4,5].includes(enumIndex("order",r.status)))return;
      if(role==="Vendor")b("Update delivery",async()=>{const text=prompt("Expected delivery date (YYYY-MM-DD):");if(!text)return;const d=new Date(`${text}T12:00:00`);if(Number.isNaN(d.getTime()))throw new Error("Enter a valid date.");await mutate(root,`/api/procurement/purchase-orders/${r.id}/delivery`,"PATCH",{expectedDeliveryAt:d.toISOString()});});
      if(role==="Storekeeper")b("Receive stock",async()=>{const text=prompt("Quantity received:",r.quantity-r.receivedQuantity);if(text===null)return;const quantity=Number(text);if(!Number.isInteger(quantity)||quantity<=0)throw new Error("Enter a positive whole quantity.");await mutate(root,`/api/procurement/purchase-orders/${r.id}/receive`,"POST",{quantityReceived:quantity});});
    });
  }
  async function showVendors(root) {
    table(root,"Vendor registrations",await list("/api/vendors/registrations"),[["Business","businessName"],["Contact","contactPerson"],["Email","email"],["Phone","mobile"],["Status",r=>status("vendor",r.status)]],(r,b)=>{
      if(enumIndex("vendor",r.status)!==0)return;
      b("Approve",()=>mutate(root,`/api/vendors/registrations/${r.id}/review`,"POST",{approve:true,rejectionReason:null}));
      b("Reject",async()=>{const reason=prompt("Reason for rejection:");if(reason===null)return;await mutate(root,`/api/vendors/registrations/${r.id}/review`,"POST",{approve:false,rejectionReason:reason});});
    });
    table(root,"Approved vendor profiles",await list("/api/vendors/profiles"),[["Business","businessName"],["Contact","contactPerson"],["Email","email"],["Specialization","specialization"]]);
  }
  async function showStaff(root) {
    table(root,"Staff accounts",await list("/api/integration/staff"),[["Employee","employeeNumber"],["Name","fullName"],["Email","email"],["Phone","phone"],["Role",r=>status("role",r.role)]]);
    form(root,"Create staff account",[field("fullName","Full name"),field("email","Email","email"),field("phone","Phone","tel"),field("employeeNumber","Employee number"),select("role","Role",numericOptions(enums.role).filter(r=>r.id>=1&&r.id<=4),r=>r.name),field("password","Initial password","password")],d=>request("/api/integration/staff","POST",{...d,role:Number(d.role)}),"Create staff account");
  }
  async function showServices(root) {
    const data=await list("/api/services?activeOnly=false");
    table(root,"Service catalogue",data,[["Name","name"],["Price",r=>money(r.basePrice)],["Minutes","estimatedDurationMinutes"],["Active",r=>r.isActive?"Yes":"No"]],(r,b)=>{
      b("Set price",async()=>{const text=prompt("Base price in LKR:",r.basePrice);if(text===null)return;const price=Number(text);if(!Number.isFinite(price)||price<0)throw new Error("Enter a valid price.");await mutate(root,`/api/services/${r.id}`,"PUT",{name:r.name,description:r.description,basePrice:price,estimatedDurationMinutes:r.estimatedDurationMinutes,isActive:r.isActive});});
      b(r.isActive?"Deactivate":"Activate",()=>mutate(root,`/api/services/${r.id}`,"PUT",{name:r.name,description:r.description,basePrice:r.basePrice,estimatedDurationMinutes:r.estimatedDurationMinutes,isActive:!r.isActive}));
    });
    form(root,"Create service package",[field("name","Package name"),field("description","Description","textarea",{optional:true}),field("basePrice","Base price (LKR)","number",{min:0}),field("estimatedDurationMinutes","Estimated minutes","number",{min:1,step:"1"})],d=>request("/api/services","POST",{...d,isActive:true}),"Create package");
  }
  async function showBays(root) {
    table(root,"Workshop bays",await list("/api/manager/bays"),[["Name","name"],["Notes","notes"]]);
    form(root,"Add workshop bay",[field("name","Bay name"),field("notes","Notes","textarea",{optional:true,maxLength:1000})],d=>request("/api/manager/bays","POST",{...d,status:0}),"Add bay");
  }
  function objectCard(root,title,data) {
    const card=document.createElement("article");card.className="sd-dashboard-card all-card";const h=document.createElement("h3");h.textContent=title;card.append(h);
    function render(value,parent){if(value && typeof value==="object"){const dl=document.createElement("dl");for(const [key,v]of Object.entries(value)){const dt=document.createElement("dt");dt.textContent=key.replace(/([a-z])([A-Z])/g,"$1 $2");dt.style.fontWeight="600";const dd=document.createElement("dd");render(v,dd);dl.append(dt,dd);}parent.append(dl);}else parent.append(document.createTextNode(String(value??"—")));}
    render(data,card);root.append(card);
  }
  async function showContact(root){table(root,"Contact enquiries",await list("/api/contact-inquiries"),[["Name","name"],["Email","email"],["Subject","subject"],["Message","message"]]);}
  const modules={
    ServiceAdvisor:[["appointments","Appointments",showAppointments],["customers","Customers",showCustomers],["jobs","Job Cards",r=>showJobs(r,"ServiceAdvisor")],["estimates","Estimates",r=>showEstimates(r,"ServiceAdvisor")],["invoices","Invoices",r=>showInvoices(r,"ServiceAdvisor")],["modifications","Modifications",r=>showModifications(r,"ServiceAdvisor")],["emergencies","Emergency",r=>showEmergency(r,"ServiceAdvisor")]],
    Manager:[["summary","Overview",async r=>objectCard(r,"Workshop overview",await request("/api/manager/dashboard"))],["jobs","Job Cards",r=>showJobs(r,"Manager")],["assignments","Assignments",showAssignments],["invoices","Invoice Approvals",r=>showInvoices(r,"Manager")],["vendors","Vendors",showVendors],["quotes","Vendor Bidding",r=>showQuotes(r,"Manager")],["orders","Purchase Orders",r=>showOrders(r,"Manager")],["staff","Staff",showStaff],["services","Services",showServices],["bays","Bays",showBays],["reports","Reports",async r=>objectCard(r,"Operational report",await request("/api/reports/operational-summary"))],["contact","Contact Enquiries",showContact]],
    Mechanic:[["jobs","Assigned Jobs",r=>showJobs(r,"Mechanic")],["completed","Completed Jobs",async r=>table(r,"Completed jobs",await list("/api/mechanic/completed-jobs"),[["Job","workOrderNumber"],["Vehicle","vehicle"],["Completed",x=>date(x.completedAt)]] )]],
    Storekeeper:[["stock","Inventory",showStock],["requisitions","Parts Requests",showRequisitions],["rfq","Vendor Requests",r=>showRFQ(r,"Storekeeper")],["orders","Receive Stock",r=>showOrders(r,"Storekeeper")]],
    Vendor:[["profile","Business Profile",async r=>{const p=await request("/api/vendors/me");objectCard(r,"Business profile",{businessName:p.businessName,contactPerson:p.contactPerson,email:p.email,mobile:p.mobile,address:p.address,specialization:p.specialization});}],["rfq","Quotation Requests",r=>showRFQ(r,"Vendor")],["quotes","My Quotations",r=>showQuotes(r,"Vendor")],["orders","Deliveries",r=>showOrders(r,"Vendor")]]
  };
  async function staffWorkspace(role) {
    const roleModules=[...(modules[role]||[]),["notifications","Notifications",showNotifications]];
    // Preserve the original dashboard header.
    let content=document.querySelector(".sd-dashboard-content,.sd-content");

    if(!content){
      const main=document.querySelector("main");
      if(!main)return;

      Array.from(main.children).forEach(child=>{
        if(!child.matches("header,.sd-topbar"))child.remove();
      });

      content=document.createElement("div");
      content.className="sd-content";
      main.append(content);
    }

    content.replaceChildren();
    const tabs=document.createElement("nav");tabs.className="all-tabs";
    const root=document.createElement("section");root.className="all-module";
    content.append(tabs,root);
    const sidebarNav=document.querySelector(".sd-sidebar-nav, aside .sd-nav");if(sidebarNav){sidebarNav.replaceChildren();tabs.classList.add("all-has-sidebar");}
    let active=roleModules[0]; let generation=0;
    const open=async module=>{
      active=module;const ticket=++generation;root.innerHTML="<p>Loading...</p>";
      document.querySelectorAll(".all-module-tab").forEach(b=>b.classList.toggle("active",b.dataset.module===module[0]));
      const title=document.getElementById("pageTitle");if(title)title.textContent=module[1];
      // Render offscreen so a slower previous request cannot overwrite another tab.
      const staging=document.createElement("div");staging.className="all-module-body";staging._host=root;staging._reload=async()=>{if(active===module)await open(module);};
      try{await module[2](staging);if(ticket===generation)root.replaceChildren(staging);}catch(e){if(ticket===generation){root.replaceChildren();notice(root,e.message,true);}}
    };
    currentReload=()=>open(active);
    for(const m of roleModules){for(const nav of [tabs,sidebarNav].filter(Boolean)){const b=document.createElement("button");b.type="button";b.textContent=m[1];b.className=nav===tabs?"sd-secondary-button all-module-tab":"sd-nav-item all-module-tab";b.dataset.module=m[0];b.onclick=()=>open(m);nav.append(b);}}
    action(content,"Refresh",()=>currentReload(),tabs);
    const sidebar=document.querySelector("aside");
    for(const id of ["sidebarOpen","menuButton"]){const b=document.getElementById(id);if(b)b.onclick=()=>sidebar?.classList.add("open");}
    const close=document.getElementById("sidebarClose");if(close)close.onclick=()=>sidebar?.classList.remove("open");
    await open(active);
  }
  async function publicForms() {
    document.addEventListener("submit",async e=>{
      const f=e.target;if(!["contactForm","vendorRegisterForm","forgotPasswordForm"].includes(f.id))return;
      e.preventDefault();e.stopImmediatePropagation();if(f.dataset.saving)return;
      const value=id=>document.getElementById(id)?.value.trim()||"";
      if(!f.reportValidity())return;
      const b=f.querySelector('button[type="submit"]');f.dataset.saving="true";if(b)b.disabled=true;
      try{
        if(f.id==="contactForm"){
          await request("/api/contact-inquiries","POST",{name:value("contactName"),email:value("contactEmail"),phone:value("contactPhone"),type:value("contactType"),subject:value("contactSubject"),message:value("contactMessageInput")});notice(f,"Your enquiry has been saved.");
        }else if(f.id==="vendorRegisterForm"){
          const password=document.getElementById("vendorPassword")?.value||"";
          const confirmInput=document.getElementById("confirmVendorPassword");
          if(confirmInput&&confirmInput.value!==password)throw new Error("Passwords do not match.");
          const terms=document.getElementById("vendorTerms");if(terms&&!terms.checked)throw new Error("Accept the terms before registering.");
          await request("/api/vendors/registrations","POST",{businessName:value("businessName"),contactPerson:value("contactPerson"),mobile:value("vendorMobile"),email:value("vendorEmail"),address:value("businessAddress"),specialization:value("specialization"),password});notice(f,"Registration saved. A manager must approve your account before login.");
        }else{
          await request("/api/auth/forgot-password","POST",{email:value("email")});notice(f,"If the account exists, a reset link will be sent when email is configured. In Development without email, check the backend console for the link.");
        }
      }catch(error){notice(f,error.message,true);}finally{delete f.dataset.saving;if(b)b.disabled=false;}
    },true);
  }
  async function identity() {
    const auth=api.auth();if(!auth)return;
    let u=auth;try{const me=await request("/api/auth/me");u={...auth,fullName:me.fullName,email:me.email,phone:me.phone,role:me.role,customerId:me.customerId};api.save(u);}catch{}
    document.querySelectorAll(".sd-sidebar-user strong,.sd-user-card strong,.sd-manager-card strong,.sd-topbar-user strong,.sd-topbar-profile strong,.sd-topbar-profile-text strong").forEach(e=>e.textContent=u.fullName||"Account");
    document.querySelectorAll(".sd-sidebar-user > div:last-child > span,.sd-user-card > div:last-child > span,.sd-manager-card > div:last-child > span,.sd-topbar-user > div:last-child > span,.sd-topbar-profile-text > span").forEach(e=>e.textContent=u.email||"");
  }
  async function initialize() {
    const style=document.createElement("style");style.textContent=".all-card{margin:16px 0;padding:20px;border:1px solid #e2e8f0;border-radius:12px;background:white}.all-card th,.all-card td{padding:10px;text-align:left;border-bottom:1px solid #e2e8f0;vertical-align:top}.all-tabs{display:flex;flex-wrap:wrap;gap:8px;margin-bottom:20px}.all-module .sd-form-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:14px;margin:16px 0}.all-module-tab.active{outline:2px solid #94a3b8}.all-module dl{display:grid;gap:8px}.all-module dd{margin-left:16px}.sd-sidebar-nav button{width:100%;text-align:left;border:0;cursor:pointer}";document.head.append(style);
    publicForms();identity();
    const path=location.pathname;const auth=api.auth();
    document.addEventListener("click",e=>{
      const a=e.target instanceof Element?e.target.closest('.sd-logout-link,[data-logout],a[href="../staff-login.html"],a[href="../login.html"]'):null;
      if(a){e.preventDefault();e.stopImmediatePropagation();api.clear();location.href=path.includes("/customer/")?"../login.html":"../staff-login.html";}
    },true);
    const staffMatch=path.match(/\/(advisor|manager|mechanic|storekeeper|vendor)\/dashboard\.html$/);
    if(staffMatch){const roles={advisor:"ServiceAdvisor",manager:"Manager",mechanic:"Mechanic",storekeeper:"Storekeeper",vendor:"Vendor"};const expected=roles[staffMatch[1]];
      if(!auth||(auth.role!==expected&&!(expected==="Manager"&&auth.role==="Admin"))){location.href="../staff-login.html";return;}
      await staffWorkspace(expected);return;
    }
    if(path.includes("/customer/")){
      if(!auth||auth.role!=="Customer"){location.href="../login.html";return;}
      const mapping={history:showHistory,tracker:showTracker,estimates:r=>showEstimates(r,"Customer"),payments:r=>showInvoices(r,"Customer"),modifications:r=>showModifications(r,"Customer"),"emergency-history":r=>showEmergency(r,"Customer")};
      for(const [id,load]of Object.entries(mapping)){const root=replaceSection(id);if(root){const reload=async()=>{root.replaceChildren();action(root,"Refresh",reload);try{await load(root);}catch(e){notice(root,e.message,true);}};root._reload=reload;await reload();}}
      const notify=document.querySelector(".sd-notification-button");
      if(notify){const root=document.createElement("div");root.hidden=true;document.querySelector(".sd-dashboard-content")?.prepend(root);
        root._reload=async()=>{root.replaceChildren();try{await showNotifications(root);}catch(e){notice(root,e.message,true);}};
        notify.onclick=async()=>{root.hidden=!root.hidden;if(!root.hidden)await root._reload();};}

    }
    if(path.endsWith("/emergency.html")){
      const providerList=document.getElementById("garageList");
      if(providerList){
        providerList.innerHTML="<p>Loading emergency providers...</p>";
        try{const providers=await list("/api/emergency/services");providerList.replaceChildren();
          table(providerList,"Available providers",providers,[["Provider","name"],["Phone","phone"],["Category","category"]]);
          const count=document.getElementById("garageCount");if(count)count.textContent=providers.length;
        }catch(e){providerList.textContent=e.message;}
      }
      const main=document.querySelector("main");if(main){const root=document.createElement("section");root.className="all-module";root.style.cssText="max-width:1000px;margin:20px auto;padding:20px";main.prepend(root);if(!auth||auth.role!=="Customer"){root.innerHTML='<p><a href="login.html">Log in as a customer</a> to submit an emergency request.</p>';}else{currentReload=async()=>{root.replaceChildren();try{await showEmergency(root,"Customer");}catch(e){notice(root,e.message,true);}};await currentReload();}}
    }
  }
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",()=>initialize().catch(e=>console.error("Integration failed:",e)),{once:true});
  else initialize().catch(e=>console.error("Integration failed:",e));
})();