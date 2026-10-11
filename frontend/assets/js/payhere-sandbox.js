(() => {
  'use strict';
  const endpoint = 'https://sandbox.payhere.lk/pay/checkout';
  const allowed = new Set(['merchant_id','order_id','amount','currency','hash','return_url','cancel_url','notify_url',
    'items','first_name','last_name','email','phone','address','city','country']);
  function redirect(checkout) {
    if (checkout?.action !== endpoint || !checkout.fields || checkout.fields.currency !== 'LKR') {
      throw new Error('Unexpected Sandbox checkout response.');
    }
    const form = document.createElement('form');
    form.method = 'POST'; form.action = endpoint;
    for (const [name,value] of Object.entries(checkout.fields)) {
      if (!allowed.has(name) || typeof value !== 'string') throw new Error('Invalid checkout field.');
      const input = document.createElement('input');
      input.type = 'hidden'; input.name = name; input.value = value; form.append(input);
    }
    document.body.append(form);
    // Keep the normal browser Referer header; PayHere validates the registered frontend domain.
    HTMLFormElement.prototype.submit.call(form);
  }
  async function showReturn(host) {
    const query = new URLSearchParams(location.search);
    const order = query.get('payhereOrder');
    if (!order || !/^SD-SBX-[a-f0-9]{32}$/i.test(order)) return;
    const card = document.createElement('section'); card.className = 'wf-card';
    const title = document.createElement('h3'); title.textContent = 'PayHere Sandbox result';
    const text = document.createElement('p'); text.setAttribute('role','status');
    const retry = document.createElement('button'); retry.type = 'button'; retry.className = 'wf-btn'; retry.textContent = 'Check payment status';
    card.append(title,text,retry); host.prepend(card);
    let busy = false, interval, count = 0;
    const check = async (refreshInvoice = false) => {
      if (busy) return; busy = true; retry.disabled = true;
      try {
        const result = await window.ShiftApi.request('/api/payhere/orders/' + encodeURIComponent(order) + '/verify', { method: 'POST' });
        text.textContent = result.message;
        if (result.status !== 'Pending' || result.message.includes('staff review')) clearInterval(interval);
        if (refreshInvoice && result.status === 'Completed' && document.body.dataset.wfModule === 'payments') {
          document.dispatchEvent(new Event('shyft:notifications-refresh'));
          document.querySelector('.wf-page-head button')?.click();
        }
      } catch (e) { text.textContent = e.message; clearInterval(interval); }
      finally { busy = false; retry.disabled = false; }
    };
    retry.onclick = () => check(true);
    await check();
    if (query.get('checkoutCancelled') === '1' && !text.textContent.startsWith('Sandbox payment verified')) {
      text.textContent += ' You returned from a cancelled checkout. Use Check payment status if you completed payment. A browser return alone cannot record payment.';
    }
    if (text.textContent.startsWith('No verified successful')) {
      interval = setInterval(() => {
        if (!card.isConnected || ++count > 20) { clearInterval(interval); return; }
        check(true);
      }, 3000);
    }
  }
  window.ShyftPayHere = Object.freeze({ redirect, showReturn });
})();
