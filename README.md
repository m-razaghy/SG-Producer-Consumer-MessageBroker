Message Broker API

📌 Overview

This is a high-performance Message Broker API built with ASP.NET Core, designed to facilitate communication between producers and consumers. It ensures message ordering, reliability, and durability using a priority queue with sequence tracking and fault-tolerant file storage.

✨ Key Features

Deterministic Producer-Consumer Mapping

Fault-Tolerant Storage (Main & Backup Files for Messages & Sequences)

Strict Message Ordering (Priority Queue per Producer-Consumer pair)

Thread-Safe Handling (Locks are minimized for better performance)

RESTful API (Endpoints for Sending, Receiving, and Acknowledging Messages)

Description:
There are some folders in this repository that are explained below:
ClassLibrary:
The generic Consumer and Producer, Logger, Form of messages like Message payload and Ack payload are implemented here and referenced in other projects.
Consumer:
The Consumer is implemented here that gets the address of the dll files and AutoFixture dll(that is used to generate random numbers) and the broker url
and receives messages via receive api in number of rate limit threads that is defined by value of rate limit attribute in dll files and if the broker 
is down or any network problems occur it will retry using the value of retry number attribute. if the retry number finishes it checks the connection
every 5 seconds.
MessageBroker:
The Message Broker is implemented here that uses some files like messages to save the messages and if it becomes offline and later becomes online it will
still hold the messages for the consumer. Also it holds the sequence number for every pair of producer and consumer to make sure the packets are arrived
in order for the consumer. The saving files is made sure is robust in the way that before saving file the main file is backed up and after the saving file
the backup file is removed also if the app is down in the middle of this process the app will load from the backup file that is not corrupted.
Producer:
The Producer is implemented here that is same to the consumer implementation only it produces classes that are found in dll files that have implemented
IProducer interface.
Samples:
In this folder there are two types IntProducer1, IntConsumer1 and IntProducer2 and IntConsumer2 that implement IProducer and IConsumer interface and the
type that is produced or consumed is integer. Also the dll files of these projects are located in Dlls for convenience.

At last the logs are implemented in info, warning and error categories that are shown by colors blue, yellow and red. Also the log files do exist in debug
folders of projects. The producer and consumer do know their address and the address of the opposite role of themselves in which the name of classes are
used. As the producer and consumer has their address so the problem of multiple consumers for one producer will not occur although not tested in this
project. Finally The message broker recognizes if the producer sends a sequence number multiple times and ignores it.
