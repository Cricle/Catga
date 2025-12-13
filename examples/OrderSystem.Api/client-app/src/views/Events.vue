<template>
  <div class="events-page">
    <va-card>
      <va-card-title>Event Sourcing & CQRS</va-card-title>
      <va-card-content>
        <va-alert color="info" class="mb-4">
          <template #icon><va-icon name="info" /></template>
          <strong>Event Sourcing</strong>
          <p>All changes are stored as immutable events. The current state is derived by replaying events.</p>
        </va-alert>

        <!-- Architecture Diagram -->
        <div class="architecture-section">
          <h4>Catga CQRS Architecture</h4>
          <div class="arch-diagram">
            <div class="arch-column">
              <div class="arch-box command">
                <va-icon name="edit" />
                <span>Commands</span>
                <small>CreateOrder, PayOrder, etc.</small>
              </div>
              <div class="arch-box query">
                <va-icon name="search" />
                <span>Queries</span>
                <small>GetOrder, GetStats, etc.</small>
              </div>
            </div>
            <div class="arch-arrow">
              <va-icon name="arrow_forward" />
            </div>
            <div class="arch-column">
              <div class="arch-box mediator">
                <va-icon name="hub" />
                <span>ICatgaMediator</span>
                <small>Route & Pipeline</small>
              </div>
            </div>
            <div class="arch-arrow">
              <va-icon name="arrow_forward" />
            </div>
            <div class="arch-column">
              <div class="arch-box handler">
                <va-icon name="code" />
                <span>Handlers</span>
                <small>Business Logic</small>
              </div>
            </div>
            <div class="arch-arrow">
              <va-icon name="arrow_forward" />
            </div>
            <div class="arch-column">
              <div class="arch-box event">
                <va-icon name="bolt" />
                <span>Events</span>
                <small>OrderCreated, etc.</small>
              </div>
            </div>
          </div>
        </div>

        <va-divider />

        <!-- Event Types -->
        <h4>Supported Event Types</h4>
        <div class="event-types">
          <div class="event-type">
            <va-badge text="OrderCreated" color="success" />
            <p>Triggered when a new order is created</p>
          </div>
          <div class="event-type">
            <va-badge text="OrderPaid" color="info" />
            <p>Triggered when payment is processed</p>
          </div>
          <div class="event-type">
            <va-badge text="OrderShipped" color="primary" />
            <p>Triggered when order is shipped</p>
          </div>
          <div class="event-type">
            <va-badge text="OrderCancelled" color="danger" />
            <p>Triggered when order is cancelled</p>
          </div>
          <div class="event-type">
            <va-badge text="OrderConfirmed" color="secondary" />
            <p>Triggered after Flow completion</p>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <!-- Features -->
    <va-card class="mt-4">
      <va-card-title>Catga Features</va-card-title>
      <va-card-content>
        <div class="features-grid">
          <div class="feature-card">
            <va-icon name="bolt" color="warning" size="large" />
            <h5>Zero Reflection</h5>
            <p>Source generator based - fully AOT compatible</p>
          </div>
          <div class="feature-card">
            <va-icon name="layers" color="primary" size="large" />
            <h5>Pipeline Behaviors</h5>
            <p>Logging, Validation, Retry, Tracing</p>
          </div>
          <div class="feature-card">
            <va-icon name="account_tree" color="success" size="large" />
            <h5>Flow DSL</h5>
            <p>Saga pattern with compensation</p>
          </div>
          <div class="feature-card">
            <va-icon name="sync" color="secondary" size="large" />
            <h5>Multi-Transport</h5>
            <p>InMemory, Redis, NATS</p>
          </div>
          <div class="feature-card">
            <va-icon name="storage" color="info" size="large" />
            <h5>Multi-Persistence</h5>
            <p>InMemory, Redis, NATS, SQLite</p>
          </div>
          <div class="feature-card">
            <va-icon name="insights" color="danger" size="large" />
            <h5>OpenTelemetry</h5>
            <p>Built-in distributed tracing</p>
          </div>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>

<script setup lang="ts">
</script>

<style scoped>
.architecture-section {
  margin: 1rem 0;
}

.arch-diagram {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 2rem;
  background: #f8fafc;
  border-radius: 8px;
  overflow-x: auto;
}

.arch-column {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.arch-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 1rem;
  border-radius: 8px;
  min-width: 120px;
  text-align: center;
}

.arch-box span {
  font-weight: 600;
  margin: 0.25rem 0;
}

.arch-box small {
  font-size: 0.7rem;
  opacity: 0.8;
}

.command { background: #dbeafe; color: #1e40af; }
.query { background: #dcfce7; color: #166534; }
.mediator { background: #fef3c7; color: #92400e; }
.handler { background: #e0e7ff; color: #3730a3; }
.event { background: #fce7f3; color: #9d174d; }

.arch-arrow {
  color: #94a3b8;
}

.event-types {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
  margin-top: 1rem;
}

.event-type {
  padding: 1rem;
  background: #f8fafc;
  border-radius: 8px;
}

.event-type p {
  margin: 0.5rem 0 0;
  font-size: 0.875rem;
  color: #666;
}

.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
}

.feature-card {
  text-align: center;
  padding: 1.5rem;
  background: #f8fafc;
  border-radius: 8px;
  transition: transform 0.2s ease;
}

.feature-card:hover {
  transform: translateY(-2px);
}

.feature-card h5 {
  margin: 0.75rem 0 0.25rem;
}

.feature-card p {
  margin: 0;
  font-size: 0.875rem;
  color: #666;
}

.mt-4 { margin-top: 1rem; }
.mb-4 { margin-bottom: 1rem; }
</style>
