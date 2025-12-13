import http from 'k6/http';
import { check, sleep } from 'k6';

// Configuration
const API_URL = __ENV.API_URL || 'http://localhost:8080';
const DURATION = __ENV.DURATION || '30s';
const VUS = __ENV.VUS || 10;

export const options = {
  stages: [
    { duration: '10s', target: VUS },        // Ramp-up
    { duration: DURATION, target: VUS },     // Stay at target
    { duration: '10s', target: 0 },          // Ramp-down
  ],
  thresholds: {
    http_req_duration: ['p(95)<1000', 'p(99)<2000'],
    http_req_failed: ['rate<0.1'],
  },
};

export default function () {
  // Test 1: Health check
  let res = http.get(`${API_URL}/health`);
  check(res, {
    'health status is 200': (r) => r.status === 200,
    'health response time < 500ms': (r) => r.timings.duration < 500,
  });

  sleep(1);

  // Test 2: Login
  res = http.post(`${API_URL}/api/auth/login`, JSON.stringify({
    email: 'customer@ordersystem.local',
    password: 'customer123',
  }), {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'login status is 200': (r) => r.status === 200,
    'login response time < 1000ms': (r) => r.timings.duration < 1000,
  });

  let token = '';
  if (res.status === 200) {
    try {
      const body = JSON.parse(res.body);
      token = body.token;
    } catch (e) {
      console.error('Failed to parse login response:', e);
    }
  }

  sleep(1);

  // Test 3: Create order
  res = http.post(`${API_URL}/api/orders`, JSON.stringify({
    customerId: `customer-${__VU}-${__ITER}`,
    items: [
      {
        productId: 'prod-1',
        quantity: 1,
        price: 99.99,
      },
    ],
    totalAmount: 99.99,
  }), {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'create order status is 200': (r) => r.status === 200,
    'create order response time < 1000ms': (r) => r.timings.duration < 1000,
  });

  sleep(1);

  // Test 4: Get order stats
  res = http.get(`${API_URL}/api/orders/stats`);

  check(res, {
    'get stats status is 200': (r) => r.status === 200,
    'get stats response time < 500ms': (r) => r.timings.duration < 500,
  });

  sleep(1);
}
