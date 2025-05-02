import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';

interface HubConnectionContextProps {
  connection: signalR.HubConnection | null;
  connectionState: signalR.HubConnectionState;
  startConnection: () => Promise<void>;
  stopConnection: () => Promise<void>;
  connectionError: string | null;
}

const HubConnectionContext = createContext<HubConnectionContextProps>({
  connection: null,
  connectionState: signalR.HubConnectionState.Disconnected,
  startConnection: async () => {},
  stopConnection: async () => {},
  connectionError: null
});

export const useHubConnection = () => useContext(HubConnectionContext);

export const HubConnectionProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [connection, setConnection] = useState<signalR.HubConnection | null>(null);
  const [connectionState, setConnectionState] = useState<signalR.HubConnectionState>(
    signalR.HubConnectionState.Disconnected
  );
  const [connectionError, setConnectionError] = useState<string | null>(null);

  // Use useCallback to memoize the startConnection function
  const startConnection = useCallback(async () => {
    if (!connection) return;
    
    try {
      if (connection.state === signalR.HubConnectionState.Disconnected) {
        console.log('Attempting to establish SignalR connection...');
        await connection.start();
        setConnectionState(connection.state);
        setConnectionError(null);
        console.log('SignalR connection established successfully');
      }
    } catch (err: any) {
      console.error('Error establishing SignalR connection:', err);
      setConnectionError(err.message || 'Failed to connect to the server');
      setConnectionState(signalR.HubConnectionState.Disconnected);
      setTimeout(startConnection, 5000); // Try to reconnect after 5 seconds
    }
  }, [connection]);

  const stopConnection = async () => {
    if (!connection) return;
    
    try {
      await connection.stop();
      setConnectionState(connection.state);
      console.log('SignalR connection stopped');
    } catch (err: any) {
      console.error('Error stopping connection:', err);
      setConnectionError(err.message || 'Failed to stop connection');
    }
  };

  useEffect(() => {
    const hubUrl = process.env.REACT_APP_HUB_URL || 'http://localhost:5000/translationHub';
    
    console.log('Initializing SignalR connection to:', hubUrl);
    
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        skipNegotiation: false,
        // Enable all transport types to improve connection reliability
        transport: signalR.HttpTransportType.WebSockets | 
                  signalR.HttpTransportType.ServerSentEvents | 
                  signalR.HttpTransportType.LongPolling,
        logMessageContent: true  // Enable detailed logging
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 15000, 30000]) // Progressive retry strategy
      .configureLogging(signalR.LogLevel.Debug) // Increase logging level for troubleshooting
      .build();

    setConnection(newConnection);

    newConnection.onclose((error) => {
      setConnectionState(signalR.HubConnectionState.Disconnected);
      if (error) {
        console.error('SignalR connection closed with error:', error);
        setConnectionError(`Connection closed: ${error}`);
      } else {
        console.log('SignalR connection closed');
      }
    });

    newConnection.onreconnecting((error) => {
      setConnectionState(signalR.HubConnectionState.Reconnecting);
      console.log('SignalR connection reconnecting...');
      if (error) {
        console.error('Reconnecting error:', error);
        setConnectionError(`Reconnecting: ${error}`);
      }
    });

    newConnection.onreconnected((connectionId) => {
      setConnectionState(signalR.HubConnectionState.Connected);
      setConnectionError(null);
      console.log('SignalR connection reestablished with ID:', connectionId);
    });

    // Start connection when component mounts
    startConnection();

    // Clean up on unmount
    return () => {
      if (newConnection) {
        console.log('Cleaning up SignalR connection...');
        newConnection.stop().catch(err => console.error('Error stopping connection:', err));
      }
    };
  }, []); // Remove startConnection from dependencies to avoid re-creating connection

  return (
    <HubConnectionContext.Provider value={{ connection, connectionState, startConnection, stopConnection, connectionError }}>
      {children}
    </HubConnectionContext.Provider>
  );
};
