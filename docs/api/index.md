# API Reference

Welcome to the Catga API Reference documentation.

## Core APIs

- **[Mediator API](./mediator.md)** - ICatgaMediator interface and methods
- **[Messages API](./messages.md)** - IRequest, IEvent, and IMessage interfaces

## Overview

Catga provides a clean and efficient API for building CQRS and event-driven applications:

- **ICatgaMediator** - The main entry point for sending commands, queries, and publishing events
- **IRequest<TResponse>** - Base interface for command and query messages
- **IEvent** - Base interface for domain events
- **IEventHandler<TEvent>** - Handler for processing events
- **IRequestHandler<TRequest, TResponse>** - Handler for processing requests

For detailed information about each API, please refer to the specific documentation pages.

